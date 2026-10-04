using System.Collections.Generic;

namespace FamiconWars.Core
{
    public enum UnitAction { Wait, Attack, Capture, Load, Join, Unload, Supply }

    /// <summary>Every change to the game is one of these. Small and serializable, so it can be sent over the network.</summary>
    public abstract class Command
    {
        public Army Army;
    }

    public sealed class UnitCommand : Command
    {
        public int UnitId;
        public int ToX, ToY;
        public UnitAction Action;
        public int TargetId = -1;   // Attack
        public int CargoId = -1;    // Unload
        public int DropX, DropY;    // Unload
    }

    public sealed class ProduceCommand : Command
    {
        public int X, Y;
        public string UnitType;
    }

    public sealed class ResupplyAllCommand : Command { }
    public sealed class EndPhaseCommand : Command { }
    public sealed class SurrenderCommand : Command { }

    // ---- events for presentation ----
    public abstract class GameEvent { }
    public sealed class MovedEvent : GameEvent { public int UnitId; public List<int> Path; }
    public sealed class BattleEvent : GameEvent
    {
        public int AttackerId, DefenderId, ToDefender, ToAttacker;
        public bool Counter, AttackerDestroyed, DefenderDestroyed;
        public int AttackerCountBefore, DefenderCountBefore, AttackerCountAfter, DefenderCountAfter;
    }
    public sealed class CaptureEvent : GameEvent { public int UnitId, X, Y, Progress, Goal; public bool Completed; }
    public sealed class ProducedEvent : GameEvent { public int UnitId; }
    /// <summary>UnitId joined into IntoId. Machines over 10 are lost.</summary>
    public sealed class JoinedEvent : GameEvent { public int UnitId, IntoId, CountA, CountB, CountAfter, Overflow; }
    public sealed class UnitLostEvent : GameEvent { public int UnitId; public string Reason; }
    public sealed class PhaseStartEvent : GameEvent { public Army Army; public int Day, Income; }
    public sealed class ResuppliedEvent : GameEvent { public int Cost; public List<int> UnitIds = new List<int>(); }
    public sealed class GameOverEvent : GameEvent { public Army Winner; public string Reason; }

    public sealed class ApplyResult
    {
        public bool Ok => Error == null;
        public string Error;
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public static ApplyResult Fail(string e) => new ApplyResult { Error = e };
    }
}
