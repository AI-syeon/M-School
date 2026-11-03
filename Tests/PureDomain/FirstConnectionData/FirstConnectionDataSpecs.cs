using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCampus.Contracts;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Compilation;
using MagicCampus.Content.Parsing;
using MagicCampus.UnityAdapters.ContentIO;
using MagicCampus.UnityAdapters.Storage;
using MagicCampus.Runtime;
using MagicCampus.State;
using MagicCampus.Transactions.Core;
using MagicCampus.Application.Reservations;
using MagicCampus.Application.Schedules;
using MagicCampus.World.Movement;
using MagicCampus.Persistence;

namespace MagicCampus.Tests.FirstConnectionData
{
    // Codex cross-module verification with the actual schemas and whole fixture, not a substitute save format.
    public static partial class FirstConnectionDataSpecs
    {
        const string Root = "Content/Fixtures/FirstConnection/generated/";
        static readonly JsonDataCodec Codec = new JsonDataCodec();
        static readonly StandardSchemaValidator Schema = new StandardSchemaValidator(Codec);
        static ContentBundle Read(string file) { var r = Codec.Parse(file, File.ReadAllBytes(file)); Check(r.Succeeded, "parse " + file); return r.Value; }
        static void Check(bool ok, string what) { if(!ok) throw new InvalidOperationException("FirstConnectionData: " + what); }
        static string S(DataNode row,string key) => row.Members[key].Text;
        static DataNode SchemaRoot => Read(Root+"base/schemas/save.schema.json").Root;
        static Outcome<ICatalog> Compile(string runner) => new CatalogCompiler(new CallParser(),Schema).Compile(Read(Root+"base/full-catalog.json"),Read(Root+runner),Read(Root+"base/schemas/functions.json"),Read(Root+"base/schemas/catalog.schema.json"),Read(Root+"base/schemas/runner.schema.json"));
        public static void Run()
        {
            foreach(var variant in new[] {"base/runner-bundle.json","variants/dest-moved/runner-bundle.json","variants/a10-mira/runner-bundle.json","variants/unregistered-effect/runner-bundle.json"})
            {
                var result=Compile(variant); Check(result.Succeeded,"compile "+variant+" "+string.Join("; ",result.Result.Details.Select(d=>d.Code+":"+d.Message)));
                if(variant.StartsWith("variants/unregistered")) continue;
                var saveFiles=variant.Contains("a10-mira") ? new[]{"variants/a10-mira/save-offered.json","variants/a10-mira/save-accepted.json"} : new[]{"base/save-offered.json","base/save-accepted.json","base/save-accepted-all.json","base/save-intro-false.json"};
                foreach(var save in saveFiles) { var problems=new SaveStateValidator(result.Value,SchemaRoot,Schema).Validate(new StateSnapshot(0,Read(Root+save).Root)); Check(problems.Count==0,"save "+save+" "+string.Join("; ",problems.Select(d=>d.Code+":"+d.Message))); }
            }
            var negatives = new Dictionary<string,string>{{"say-arg-count","ARG_COUNT"},{"wait-arg-type","ARG_TYPE"},{"unknown-function","UNKNOWN_FUNCTION"},{"move-ref-not-found","REF_NOT_FOUND"},{"move-ref-kind","REF_KIND"}};
            foreach(var folder in Directory.GetDirectories(Root+"variants/invalid"))
            {
                var o=Compile("variants/invalid/"+Path.GetFileName(folder)+"/runner-bundle.json");
                Check(!o.Succeeded && o.Result.Details.Any(d=>d.Code==negatives[Path.GetFileName(folder)] && d.Location!=null && d.Location.Pointer.Contains("/events/")),"invalid fixture rejected with expected code/source pointer "+folder+" "+string.Join(";",o.Result.Details.Select(d=>d.Code)));
            }
            var compiled=Compile("base/runner-bundle.json").Value;
            var provider=new CatalogProvider(new CatalogCompiler(new CallParser(),Schema),compiled);
            var schedules=new Dictionary<string,IReadOnlyList<CompiledCall>>();
            foreach(var row in compiled.FindDefinition("runner","root").Members["schedules"].Items)
                schedules.Add(S(row,"id"),((ICompiledContentLookup)compiled).FindCalls("runner","schedules",S(row,"id"),"script"));
            var service=new ScheduleService(compiled,schedules,new CatalogDestinationResolver(provider));
            Check(service.CurrentDestination("npc.serin",new GameTime(420,0)).Value.DestinationId=="dest.serin_home","real compiled wildcard schedule at morning");
            Check(service.CurrentDestination("npc.serin",new GameTime(800,0)).Value.DestinationId=="dest.notice","real compiled current schedule");
            Check(service.CurrentDestination("npc.serin",new GameTime(1440+10,0)).Value.DestinationId=="dest.serin_home","midnight uses current day schedule");
            var initial=new StateSnapshot(0,Read(Root+"base/save-offered.json").Root);
            var registry=new StateParticipantRegistry(); registry.Register(new ReservationParticipant(provider,new StateCompletionLedger())); registry.Seal();
            var pump=new CompletionPump(); var store=new StateStore(initial,registry,new[]{new SaveStateValidator(compiled,SchemaRoot,Schema)},pump);
            var reservations=new ReservationService(provider,new MagicCampus.Timekeeping.Calendar(),new StateCompletionLedger());
            var accepted=reservations.PlanAccept("event.snack",store.Current); Check(accepted.Succeeded,"accept plan");
            Check(store.Commit(new StatePlan(new TransactionId("fixture.accept"),null,store.Current.Revision,new[]{accepted.Value})).Succeeded,"actual accept full-schema commit");
            Check(reservations.Candidates(store.Current).Any(c=>c.EventId=="event.snack"),"actual accepted candidate");
            var restorer=new SaveRestorer(Codec,Schema,SchemaRoot,c=>new SaveStateValidator(c,SchemaRoot,Schema));
            var saveCodec=new SaveCodec(Codec); var sealedSave=saveCodec.Seal(store.Current,1);
            Check(restorer.Restore(sealedSave,compiled).Succeeded,"real entire v2 seal/restore with schema and references");
            var old=DataNode.FromObject(store.Current.Root.Members.Where(p=>!SaveMigration.V1AddedFields.ContainsKey(p.Key))).With("saveVersion",DataNode.FromInteger(1)).With("generation",DataNode.FromInteger(1));
            var bytes=Codec.Serialize(old).ToArray();
            var v1=new SaveEnvelope(1,compiled.CatalogHash,1,SaveHash.Sha256Hex(bytes),bytes);
            var migrated=restorer.Restore(v1,compiled); Check(migrated.Succeeded,"representative v1 passes actual v2 schema/references after migration: "+string.Join("; ",migrated.Result.Details.Select(d=>d.Message)));
            Console.WriteLine("PASS actual fixture/schema/compiled schedules/reservation/save migration integration");
        }
        public static void RunDisk()
        {
            var compiled=Compile("base/runner-bundle.json").Value;
            var provider=new CatalogProvider(new CatalogCompiler(new CallParser(),Schema),compiled);
            var pump=new CompletionPump(); var handles=new HandleFactory(pump); var policy=new SafeBoundaryPolicy();
            var registry=new StateParticipantRegistry();registry.Seal();
            var invariant=new SaveStateValidator(compiled,SchemaRoot,Schema);
            var store=new StateStore(new StateSnapshot(0,Read(Root+"base/save-accepted.json").Root),registry,new[]{invariant},pump);
            var dir=Path.GetFullPath("Tests/Results/stage4-disk-"+Guid.NewGuid().ToString("N"));
            bool fail=false;
            var disk=new FileGenerationStore(dir,handles,0,ctx=>{if(fail && ctx.Phase==StorageFaultPhase.CreateTemp)throw new IOException("injected actual disk failure");});
            var codec=new SaveCodec(Codec);
            var restorer=new SaveRestorer(Codec,Schema,SchemaRoot,c=>new SaveStateValidator(c,SchemaRoot,Schema));
            var saves=new SaveService(policy,codec,disk,restorer,store,provider,handles);
            void Wait(IHandle h) {for(int i=0;i<200 && h.Terminal==null;i++){pump.AdvanceTick();disk.Tick(.01,.01);}pump.AdvanceTick();Check(h.Terminal!=null,"bounded disk terminal");}
            policy.Notify(SafeBoundary.SceneEntry,store.Current);
            var first=saves.SaveLatestSafe();Wait(first);Check(first.Status==OperationStatus.Succeeded,"real entry save");
            var modified=new StateSnapshot(0,store.Current.Root.With("wallet",DataNode.FromInteger(321)));
            Check(store.ReplaceAll(modified,store.Current.Revision,new TransactionId("disk.memory.success")).Succeeded,"memory success");
            policy.Notify(SafeBoundary.SceneComplete,store.Current);
            fail=true;var failed=saves.SaveLatestSafe();Wait(failed);Check(failed.Status==OperationStatus.Failed && store.Current.Slice("wallet").Integer==321 && !policy.Latest.Saved,"disk failure retains memory and unsaved flag");
            fail=false;var retry=saves.SaveLatestSafe();Wait(retry);Check(retry.Status==OperationStatus.Succeeded && policy.Latest.Saved,"disk retry saves same memory");
            var corrupt=disk.Write(retry.Value.Generation+1,new byte[]{123,125});Wait(corrupt);Check(corrupt.Status==OperationStatus.Succeeded,"valid physical generation with invalid payload injected");
            var loaded=saves.LoadLatest();Wait(loaded);Check(loaded.Status==OperationStatus.Succeeded && loaded.Value.Generation==retry.Value.Generation,"fallback to whole preceding restorable generation");
            Check(store.Current.Slice("wallet").Integer==321 && store.Current.Root.Members.Count==45,"real reload full data and exact wallet");
            var incompatible=restorer.Restore(new SaveEnvelope(2,new string('b',64),retry.Value.Generation,codec.Seal(store.Current,retry.Value.Generation).Checksum,codec.Seal(store.Current,retry.Value.Generation).Payload),compiled);
            Check(!incompatible.Succeeded && incompatible.Result.Code==ResultCodes.SaveIncompatible,"wrong catalog refused");
            Check(handles.LiveCount==0 && disk.PendingOperations==0,"real disk handle cleanup");
            File.WriteAllText(Path.Combine(dir,"verification.txt"),"PASS actual production save codec/schema/ref validator/service/generation store: entry, disk failure, retry, fallback, whole reload, incompatible hash, no leaks");
            Console.WriteLine("PASS production disk integration "+dir);
        }
    }
}
