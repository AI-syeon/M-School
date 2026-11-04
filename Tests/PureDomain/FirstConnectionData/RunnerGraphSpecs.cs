using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MagicCampus.Contracts;
using MagicCampus.Content.Catalog;
using MagicCampus.Content.Compilation;
using MagicCampus.Content.Parsing;
using MagicCampus.UnityAdapters.Storage;
using MagicCampus.Runtime;
using MagicCampus.State;
using MagicCampus.Transactions.Core;
using MagicCampus.Transactions.Conditions;
using MagicCampus.Transactions.Effects;
using MagicCampus.Application.Reservations;
using MagicCampus.Application.Schedules;
using MagicCampus.World.Movement;
using MagicCampus.Persistence;
using MagicCampus.Events;
using MagicCampus.Tests.Movement;

namespace MagicCampus.Tests.FirstConnectionData
{
    public static partial class FirstConnectionDataSpecs
    {
        sealed class GraphPresentation : IPresentationPort, IPresentationLifetime
        {
            readonly IHandleFactory handles; readonly IActorWorld world; readonly Destination target;
            readonly List<IHandle> owned = new List<IHandle>(); int serial;
            public readonly List<string> Said=new List<string>();
            public IHandleController<ChoiceSelection> Choice;
            public GraphPresentation(IHandleFactory handles,IActorWorld world,Destination target){this.handles=handles;this.world=world;this.target=target;}
            public IHandle Say(string speaker,string text,string owner)
            {
                Check(new MoveArrival(world.Find(speaker).Position,target).Arrived,"real movement reaches target before say");
                Said.Add(text);var h=handles.Create<bool>("graph.say."+(++serial));h.Start();h.SetCancellationHandler(r=>h.Cancelled(r,0));h.Succeed(true,0);owned.Add(h);return h;
            }
            public IHandle<ChoiceSelection> Choose(string prompt,IReadOnlyList<ChoiceOption> options,string owner)
            {
                Choice=handles.Create<ChoiceSelection>("graph.choice."+(++serial));Choice.Start();Choice.SetCancellationHandler(r=>Choice.Cancelled(r,0));owned.Add(Choice);return Choice;
            }
            public IHandle MoveCamera(Destination target,double seconds,string owner)=>throw new NotSupportedException("not used by this fixture");
            public IHandle Animate(string actor,string clip,double seconds,string owner)=>throw new NotSupportedException("not used by this fixture");
            public void CloseOwner(string owner){foreach(var h in owned)if(h.Terminal==null)h.Cancel("owner closed");}
        }
        public static void RunGraph()
        {
            foreach(bool cancel in new[]{false,true})
            {
                var compiled=Compile("base/runner-bundle.json").Value;
                var provider=new CatalogProvider(compiled);
                var initial=new StateSnapshot(0,Read(Root+"base/save-accepted.json").Root);
                var pump=new CompletionPump();var handles=new HandleFactory(pump);var pause=new PauseService();var leases=new LeaseService();var ticks=new TickClock(handles);
                var grid=new TestWorld();var maps=new TestMapPort(handles,"map.courtyard");
                foreach(var row in compiled.FindDefinition("runner","root").Members["maps"].Items)
                {
                    var width=(int)row.Members["width"].Integer;var height=(int)row.Members["height"].Integer;
                    var rows=Enumerable.Range(0,height).Select(y=>Enumerable.Repeat('.',width).ToArray()).ToArray();
                    foreach(var b in row.Members["blocked"].Items)rows[(int)b.Items[1].Integer][(int)b.Items[0].Integer]='#';
                    grid.Add(new TestGrid(S(row,"id"),rows.Select(r=>new string(r)).ToArray()));
                    foreach(var p in row.Members["portals"].Items){var to=p.Members["to"];maps.AddPortal(new Portal(S(p,"id"),new Position(S(row,"id"),p.Members["x"].Number,p.Members["y"].Number),new Position(S(to,"mapId"),to.Members["x"].Number,to.Members["y"].Number),p.Members["open"].Bool));}
                }
                double Num(DataNode n,string key)=>n.Members[key].Kind==DataKind.Integer?n.Members[key].Integer:n.Members[key].Number;
                Position Pos(DataNode n)=>new Position(S(n,"mapId"),Num(n,"x"),Num(n,"y"));
                var serin=initial.Slice("npcPositions").Items.First(n=>S(n,"npcId")=="npc.serin");
                var actors=new ActorWorld(grid,new[]{new ActorState("player",Pos(initial.Slice("player")),.2,3),new ActorState("npc.serin",Pos(serin),.2,3)});
                var moves=new MoveService(actors,grid,maps,grid,handles,pause,o=>o=="graph.scene",requiresVisibleMap:r=>true);
                var calendar=new MagicCampus.Timekeeping.Calendar();
                var registry=new StateParticipantRegistry();registry.Register(new EventEffectsParticipant(compiled));registry.Register(new WorldCommitParticipant(actors,grid));registry.Register(new MagicCampus.Timekeeping.ClockParticipant(calendar));registry.Register(new ReservationParticipant(provider,new StateCompletionLedger()));registry.Seal();
                var store=new StateStore(initial,registry,new[]{new SaveStateValidator(compiled,SchemaRoot,Schema)},pump,new PersistedCompletionReplayPolicy());
                var clock=new MagicCampus.Timekeeping.ClockService(store,calendar);var projector=new WorldStateProjector(actors);var resolver=new CatalogDestinationResolver(provider);var target=resolver.Resolve("npc.serin","dest.snack").Value;
                var dir=Path.GetFullPath("Tests/Results/stage4-graph-"+Guid.NewGuid().ToString("N"));var disk=new FileGenerationStore(dir,handles,0);var policy=new SafeBoundaryPolicy();
                var restorer=new SaveRestorer(Codec,Schema,SchemaRoot,c=>new SaveStateValidator(c,SchemaRoot,Schema),null);
                var saves=new SaveService(policy,new SaveCodec(Codec),disk,restorer,store,provider,handles);var ui=new GraphPresentation(handles,actors,target);bool hidden=false;
                var staging=new ReservedStagingService(actors,maps,grid,handles,(definition,lease)=>Outcome<ReservedJoinPlan>.Ok(new ReservedJoinPlan("map.lounge",new[]{new ReservedActorPlacement("player",new Position("map.lounge",.5,2.5)),new ReservedActorPlacement("npc.serin",new Position("map.lounge",.5,2.5))}),store.Current.Revision),value=>{if(value)Check(disk.ListCandidates().Count>0,"entry physically saved before hiding/staging");hidden=value;},null);
                var runner=new EventRunner(new EventRunnerDependencies{Catalog=provider,Conditions=new ConditionEvaluator(compiled),Leases=leases,Pauses=pause,Moves=moves,Destinations=resolver,Presentation=ui,PresentationLifetime=ui,Clock=ticks,CommitPlanner=new EventCommitPlanner(clock,projector,compiled),Store=store,Projector=projector,Recovery=new SceneRecovery(actors),Boundaries=policy,Saves=saves,Staging=staging,Maps=maps,Handles=handles,RepeatBlocked=(e,s)=>new StateCompletionLedger().IsCompleted(s,e.Completion??new CompletionKey(e.Id))});
                var handle=runner.Start(new EventStartRequest("event.snack","graph.scene",1,new TransactionId("graph.start")));
                void Frame(){pump.AdvanceTick();ticks.Advance(.1,.1);disk.Tick(.1,.1);staging.Tick(.1,.1);moves.Tick(.1,.1);runner.Tick(.1,.1);}
                for(int i=0;i<600 && ui.Choice==null && handle.Terminal==null;i++)Frame();
                Check(ui.Choice!=null,"real graph reached choice: "+(handle.Terminal?.Code??"pending"));
                if(cancel){handle.Cancel("player");handle.Cancel("duplicate");}else ui.Choice.Succeed(new ChoiceSelection("tea",1),store.Current.Revision);
                for(int i=0;i<300 && (handle.Terminal==null || runner.PendingSaveCount>0);i++)Frame();Frame();Frame();
                Check(handle.Terminal!=null && !hidden && runner.ActiveCount==0 && leases.ActiveLeaseCount==0 && handles.LiveCount==0,"real graph terminal/no leases/handles/hidden overlay");
                if(cancel)
                {
                    Check(handle.Status==OperationStatus.Cancelled && !policy.SceneActive,"real graph cancel renews safe tracking");
                    Check(store.Current.Slice("wallet").Integer==200 && clock.Now.AbsoluteMinute==420 && store.Current.Slice("appointments").Items.Any(n=>S(n,"id")=="event.snack"&&S(n,"status")=="Accepted"),"cancel preserves Accepted and no rewards/cost");
                    Check(actors.Find("player").Position.Equals(Pos(initial.Slice("player")))&&maps.ActiveMapId=="map.courtyard","real graph restores entry map/actors");
                }
                else
                {
                    Check(handle.Status==OperationStatus.Succeeded,"real graph event success "+handle.Terminal.Code);
                    Check(store.Current.Slice("wallet").Integer==220 && clock.Now.AbsoluteMinute==430,"real graph once reward and time");
                    Check(!store.Current.Slice("appointments").Items.Any(n=>S(n,"id")=="event.snack"),"real graph matching reservation removed");
                    Check(ui.Said.Count==2 && ui.Said[1].Contains("따뜻한 차"),"real graph chosen tea fork only");
                    Check(!runner.Unsaved && policy.Latest.Saved && handle.Value.Completion.Value.Value=="event.snack","real graph autosave/effective completion");
                    var reload=saves.LoadLatest();for(int i=0;i<100&&reload.Terminal==null;i++)Frame();Check(reload.Status==OperationStatus.Succeeded && store.Current.Slice("wallet").Integer==220,"real graph whole saved reload");
                }
                File.WriteAllText(Path.Combine(dir,"verification.txt"),"PASS real production Runner + MoveService + staging + conditions + effects + clock + world + reservations + scene recovery + safe policy + real save service/disk, display/map geometry ports are test doubles; cancel="+cancel);
            }
            Console.WriteLine("PASS production Runner graph success/cancel with physical saves and full schema");
        }
    }
}
