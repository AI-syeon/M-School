using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MagicCampus.Contracts;
using MagicCampus.Runtime;
using MagicCampus.World.Movement;
using MagicCampus.UnityAdapters.Maps;
using MagicCampus.UnityAdapters.Collision;
using UnityEngine;

namespace MagicCampus.Tests.Stage3Player
{
    // Codex integration checks against real Unity map construction, colliders and load handles.
    public sealed class Stage3PlayerSmoke : MonoBehaviour
    {
        [Serializable] public sealed class Report { public string unityVersion; public string platform; public bool passed; public int assertions; public string error; }
        int assertions;
        MapAdapter maps;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--stage3-player-smoke") < 0) return;
            var host = new GameObject("Stage3 actual movement acceptance"); DontDestroyOnLoad(host); host.AddComponent<Stage3PlayerSmoke>();
        }
        IEnumerator Start()
        {
            yield return null;
            var report = new Report { unityVersion = UnityEngine.Application.unityVersion, platform = UnityEngine.Application.platform.ToString() };
            try { Verify(); report.passed = true; } catch(Exception e) { report.error = e.ToString(); Debug.LogException(e); }
            finally { maps?.Dispose(); }
            yield return null;
            report.assertions = assertions;
            var output = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), "stage3-player.json");
            foreach(var a in Environment.GetCommandLineArgs()) if(a.StartsWith("--result=")) output = a.Substring(9);
            File.WriteAllText(output, JsonUtility.ToJson(report, true)); UnityEngine.Application.Quit(report.passed ? 0 : 1);
        }
        void Verify()
        {
            var parsed = MapLayout.Parse(DataNode.FromList(new[] { Map("map.a", true), Map("map.b", false) }), "stage3-integration");
            Check(parsed.Succeeded, "map definitions");
            var pump = new CompletionPump(); var factory = new HandleFactory(pump); var pause = new PauseService();
            var collision = new GridCollisionPort(parsed.Value);
            maps = new MapAdapter(parsed.Value, factory, new MapAdapterConfig(null, null));
            var actors = new ActorWorld(maps, new[] { new ActorState("player", Pos("map.a", .5, 1.5), .2, 3), new ActorState("npc.serin", Pos("map.b", .5, 1.5), .2, 3) });
            var moves = new MoveService(actors, collision, maps, maps, factory, pause,
                owner => owner == "scene", requiresVisibleMap: request => request.ActorId == "player" || request.OwnerToken == "scene");
            Action frame = () => { pump.AdvanceTick(); maps.Tick(.1, .1); moves.Tick(.1, .1); };
            var load = maps.Load("map.a", "setup"); frame(); frame();
            Check(load.Status == OperationStatus.Succeeded && maps.ActiveMapId == "map.a", "real source map loaded");
            Check(maps.FindColliderMismatches().Count == 0, "colliders match grid");
            var off = moves.Move(Request("npc.serin", "map.b", 3.5, 1.5, "schedule"));
            for(int i=0;i<100 && off.Terminal == null;i++) frame();
            Check(off.Status == OperationStatus.Succeeded, "inactive NPC reaches logical destination");
            Check(maps.ActiveMapId == "map.a", "inactive NPC does not change display map");
            var token = pause.Acquire("scene", new PauseSet(true, false, true, true, false));
            var scene = moves.Move(Request("player", "map.b", 3.5, 1.5, "scene"));
            Check(scene.Terminal == null, "different map is not immediate arrival");
            for(int i=0;i<100 && scene.Terminal == null;i++) frame();
            Check(scene.Status == OperationStatus.Succeeded, "scene move crosses real portal while calendar/player/ambient paused");
            Check(maps.ActiveMapId == "map.b" && actors.Find("player").Position.MapId == "map.b", "map and logical actor agree");
            Check(new MoveArrival(actors.Find("player").Position, Request("player", "map.b", 3.5, 1.5, "scene").Destination).Arrived, "actual distance arrival");
            pause.Release(token); frame();
            var changed = moves.Move(Request("player", "map.b", .5, 3.5, "manual"));
            frame(); var last = actors.Find("player").Position; changed.Cancel("cancel"); changed.Cancel("duplicate"); frame();
            Check(changed.Status == OperationStatus.Cancelled && actors.Find("player").Position.Equals(last), "cancel preserves last valid point");
            parsed.Value.SetBlocked("map.b", 2, 3, true);
            var blocked = moves.Move(Request("player", "map.b", 2.5, 3.5, "manual"));
            Check(blocked.Status == OperationStatus.Failed && blocked.Terminal.Code == ResultCodes.TargetBlocked, "blocked actual tile refuses arrival");
            frame(); frame();
            Check(moves.ActiveMoves.Count == 0 && factory.LiveCount == 0, "all actual handles cleaned");
            Check(maps.FindColliderMismatches().Count == 0, "updated actual colliders agree");
        }
        static Position Pos(string map, double x, double y) => new Position(map,x,y);
        static MoveRequest Request(string actor,string map,double x,double y,string owner) => new MoveRequest(actor,new Destination("",Pos(map,x,y),.1),owner,30,true);
        void Check(bool ok,string message) { assertions++; if(!ok) throw new InvalidOperationException(message); }
        static DataNode Map(string id,bool portal)
        {
            return DataNode.FromObject(new Dictionary<string,DataNode> {
                ["id"]=DataNode.FromString(id),["width"]=DataNode.FromInteger(5),["height"]=DataNode.FromInteger(5),["blocked"]=DataNode.FromList(new DataNode[0]),
                ["portals"]=DataNode.FromList(portal ? new[] { DataNode.FromObject(new Dictionary<string,DataNode> {
                    ["id"]=DataNode.FromString("portal.ab"),["x"]=DataNode.FromNumber(4.5),["y"]=DataNode.FromNumber(1.5),["open"]=DataNode.True,
                    ["to"]=DataNode.FromObject(new Dictionary<string,DataNode>{["mapId"]=DataNode.FromString("map.b"),["x"]=DataNode.FromNumber(.5),["y"]=DataNode.FromNumber(1.5)})
                }) } : new DataNode[0])
            });
        }
    }
}
