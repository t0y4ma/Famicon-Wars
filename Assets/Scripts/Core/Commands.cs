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
    /// <summary>Interlude: a COM played this phase, so a 内政 time follows before the next army (set by whoever runs the game).</summary>
    public sealed class EndPhaseCommand : Command { public bool Interlude; }
    public sealed class SurrenderCommand : Command { }
    /// <summary>The online room's host ends the match (a draw). Only the server issues it, never a player's command.</summary>
    public sealed class AbortCommand : Command { }

    // ---- 内政 (off-turn: allowed while another army plays, and in the 内政 time) ----
    /// <summary>Reserve one level up of an own facility; paid now, applied at the start of the army's next phase.</summary>
    public sealed class LevelUpCommand : Command { public int X, Y; }
    /// <summary>Reserve turning an own city into Target (FACTORY, AIRPORT, PORT).</summary>
    public sealed class ConvertCommand : Command { public int X, Y; public string Target; }
    /// <summary>Take back the army's latest reservation (funds returned). Only the latest one: a newer reservation fixes the older.</summary>
    public sealed class CancelReserveCommand : Command { }
    /// <summary>A player presses 「内政終了」 in the 内政 time.</summary>
    public sealed class InterludeDoneCommand : Command { }
    /// <summary>The 内政 time is over (enough players pressed, or time ran out). Only whoever runs the game issues it.</summary>
    public sealed class EndInterludeCommand : Command { }

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
    /// <summary>
    /// An army is out while the game goes on (three or more armies). By = the army that took its base
    /// (it gets all of the loser's properties, and the base itself becomes a city at X,Y), else None
    /// (routed or surrendered: its properties turn neutral). Its remaining units are removed.
    /// </summary>
    public sealed class EliminatedEvent : GameEvent { public Army Army, By; public string Reason; public int X = -1, Y = -1; }

    /// <summary>Kind: GameState.ReserveLevelUp or ReserveConvertBase + terrain index. Cancelled: taken back by its owner (refund).</summary>
    public sealed class ReservedEvent : GameEvent { public Army Army; public int X, Y, Kind, Cost; public bool Cancelled; }
    /// <summary>An enemy started capturing a reserved facility: the reservation is gone, the funds are not returned.</summary>
    public sealed class ReserveLostEvent : GameEvent { public Army Army; public int X, Y, Cost; }
    /// <summary>A reservation took effect at the start of the owner's phase.</summary>
    public sealed class LevelChangedEvent : GameEvent { public Army Army; public int X, Y, Level; public string Converted; }
    public sealed class InterludeEvent : GameEvent { public bool Started; public Army After; }

    public sealed class ApplyResult
    {
        public bool Ok => Error == null;
        public string Error;
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public static ApplyResult Fail(string e) => new ApplyResult { Error = e };
    }
}
