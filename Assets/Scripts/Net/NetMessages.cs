using System.Collections.Generic;
using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>Message tags. Append only — these bytes are the wire format.</summary>
    public enum NetMessageKind : byte
    {
        None = 0,
        JoinRequest,
        JoinAccept,
        JoinReject,
        TurnInput,
        TurnCommit,
        DesyncHalt,
        PeerDropped,
        SlotToAi,
        SnapshotChunk,
        Heartbeat,
        LobbyState,
        StartMatch,
        /// <summary>Client's map/rules fingerprint, sent once it has been told
        /// which map the host is playing.</summary>
        IdentityConfirm,
    }

    public enum JoinRejectReason : byte
    {
        None = 0,
        ProtocolVersion,
        SimVersion,
        MapMismatch,
        RulesMismatch,
        AiProfileMismatch,
        GameFull,
        AlreadyStarted,
    }

    /// <summary>
    /// What two builds must agree on before a match can start. Lockstep has no
    /// tolerance for "nearly the same": a one-value difference in a stat table
    /// produces a divergence hundreds of turns later, presenting as a mystery
    /// desync rather than as the version mismatch it is. Every field here is
    /// cheap to compute and is checked at join time, so the failure is a refused
    /// connection naming the mismatching field.
    /// </summary>
    public struct BuildIdentity
    {
        /// <summary>The protocol itself. Bump on any wire-format change.</summary>
        public const ushort CurrentProtocolVersion = 1;

        public ushort ProtocolVersion;

        /// <summary>Sim rules/behaviour generation. Bump whenever a change alters
        /// simulation outcomes — that is the thing peers cannot disagree about.</summary>
        public uint SimVersion;

        /// <summary>FNV over the raw .pud bytes. Two players picking "the same
        /// map" routinely load different files: map paths resolve either into
        /// StreamingAssets or into the player's own Warcraft II install.</summary>
        public uint MapHash;

        /// <summary>Hash of the live RuleSet, taken after per-map UDTA/UGRD
        /// overrides — covers both stat-table drift and map-specific rules.</summary>
        public uint RulesHash;

        /// <summary>Per-slot AI profile hash. Strategies are named by string and
        /// resolved locally, so two peers can resolve one name to different
        /// files; a drop-to-AI handoff would then diverge.</summary>
        public uint AiProfileHash;

        public void Write(ref ByteWriter w)
        {
            w.WriteUShort(ProtocolVersion);
            w.WriteUInt(SimVersion);
            w.WriteUInt(MapHash);
            w.WriteUInt(RulesHash);
            w.WriteUInt(AiProfileHash);
        }

        public static BuildIdentity Read(ref ByteReader r) => new BuildIdentity
        {
            ProtocolVersion = r.ReadUShort(),
            SimVersion = r.ReadUInt(),
            MapHash = r.ReadUInt(),
            RulesHash = r.ReadUInt(),
            AiProfileHash = r.ReadUInt(),
        };

        /// <summary>
        /// The first field that disagrees, or None. Reporting the first specific
        /// mismatch — rather than a bare "incompatible" — is the difference
        /// between "you have a different map" and an evening of guessing.
        /// </summary>
        /// <summary>
        /// Only the parts a joiner can know before the host has told it which map
        /// is being played. Checked first so an incompatible build is turned away
        /// immediately, without occupying a seat.
        /// </summary>
        public JoinRejectReason CompareVersionsTo(in BuildIdentity other)
        {
            if (ProtocolVersion != other.ProtocolVersion) return JoinRejectReason.ProtocolVersion;
            if (SimVersion != other.SimVersion) return JoinRejectReason.SimVersion;
            return JoinRejectReason.None;
        }

        public JoinRejectReason CompareTo(in BuildIdentity other)
        {
            if (ProtocolVersion != other.ProtocolVersion) return JoinRejectReason.ProtocolVersion;
            if (SimVersion != other.SimVersion) return JoinRejectReason.SimVersion;
            if (MapHash != other.MapHash) return JoinRejectReason.MapMismatch;
            if (RulesHash != other.RulesHash) return JoinRejectReason.RulesMismatch;
            if (AiProfileHash != other.AiProfileHash) return JoinRejectReason.AiProfileMismatch;
            return JoinRejectReason.None;
        }
    }

    /// <summary>
    /// Framing for every packet. Length-prefixed and explicitly little-endian via
    /// ByteWriter/ByteReader, exactly like replays, so the format is identical on
    /// every platform and readable by the same tools.
    /// </summary>
    public static class NetMessages
    {
        public static void WriteTurnInput(ref ByteWriter w, byte slot, int turn,
            List<GameCommand> commands, int hashTurn, uint stateHash)
        {
            w.WriteByte((byte)NetMessageKind.TurnInput);
            w.WriteByte(slot);
            w.WriteInt(turn);
            w.WriteInt(hashTurn);
            w.WriteUInt(stateHash);
            w.WriteUShort((ushort)commands.Count);
            for (int i = 0; i < commands.Count; i++)
                commands[i].Write(ref w);
        }

        public static void ReadTurnInput(ref ByteReader r, out byte slot, out int turn,
            List<GameCommand> into, out int hashTurn, out uint stateHash)
        {
            slot = r.ReadByte();
            turn = r.ReadInt();
            hashTurn = r.ReadInt();
            stateHash = r.ReadUInt();
            int count = r.ReadUShort();
            into.Clear();
            for (int i = 0; i < count; i++)
                into.Add(GameCommand.Read(ref r));
        }

        public static void WriteTurnCommit(ref ByteWriter w, int turn, List<GameCommand> commands)
        {
            w.WriteByte((byte)NetMessageKind.TurnCommit);
            w.WriteInt(turn);
            w.WriteUShort((ushort)commands.Count);
            for (int i = 0; i < commands.Count; i++)
                commands[i].Write(ref w);
        }

        public static void ReadTurnCommit(ref ByteReader r, out int turn, List<GameCommand> into)
        {
            turn = r.ReadInt();
            int count = r.ReadUShort();
            into.Clear();
            for (int i = 0; i < count; i++)
                into.Add(GameCommand.Read(ref r));
        }

        public static void WriteJoinRequest(ref ByteWriter w, in BuildIdentity identity, string playerName)
        {
            w.WriteByte((byte)NetMessageKind.JoinRequest);
            var id = identity;
            id.Write(ref w);
            WriteString(ref w, playerName);
        }

        public static void ReadJoinRequest(ref ByteReader r, out BuildIdentity identity, out string playerName)
        {
            identity = BuildIdentity.Read(ref r);
            playerName = ReadString(ref r);
        }

        public static void WriteJoinReject(ref ByteWriter w, JoinRejectReason reason)
        {
            w.WriteByte((byte)NetMessageKind.JoinReject);
            w.WriteByte((byte)reason);
        }

        public static void WriteJoinAccept(ref ByteWriter w, byte yourSlot, LobbyPayload payload)
        {
            w.WriteByte((byte)NetMessageKind.JoinAccept);
            w.WriteByte(yourSlot);
            payload.Write(ref w);
        }

        public static void ReadJoinAccept(ref ByteReader r, out byte yourSlot, out LobbyPayload payload)
        {
            yourSlot = r.ReadByte();
            payload = LobbyPayload.Read(ref r);
        }

        public static void WriteIdentityConfirm(ref ByteWriter w, in BuildIdentity identity)
        {
            w.WriteByte((byte)NetMessageKind.IdentityConfirm);
            var id = identity;
            id.Write(ref w);
        }

        public static void WriteLobbyState(ref ByteWriter w, LobbyPayload payload)
        {
            w.WriteByte((byte)NetMessageKind.LobbyState);
            payload.Write(ref w);
        }

        public static void WriteStartMatch(ref ByteWriter w, LobbyPayload payload)
        {
            w.WriteByte((byte)NetMessageKind.StartMatch);
            payload.Write(ref w);
        }

        public static void WriteDesyncHalt(ref ByteWriter w, in DesyncReport report)
        {
            w.WriteByte((byte)NetMessageKind.DesyncHalt);
            w.WriteInt(report.Turn);
            w.WriteByte(report.Slot);
            w.WriteUInt(report.ExpectedHash);
            w.WriteUInt(report.ActualHash);
        }

        public static DesyncReport ReadDesyncHalt(ref ByteReader r)
        {
            int turn = r.ReadInt();
            byte slot = r.ReadByte();
            uint expected = r.ReadUInt();
            uint actual = r.ReadUInt();
            return new DesyncReport(turn, slot, expected, actual);
        }

        /// <summary>Length-prefixed UTF-8. No culture involved, so it round-trips
        /// identically on every machine.</summary>
        public static void WriteString(ref ByteWriter w, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                w.WriteUShort(0);
                return;
            }
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            int count = bytes.Length > ushort.MaxValue ? ushort.MaxValue : bytes.Length;
            w.WriteUShort((ushort)count);
            w.Ensure(count);
            System.Array.Copy(bytes, 0, w.Buffer, w.Position, count);
            w.Position += count;
        }

        public static string ReadString(ref ByteReader r)
        {
            int count = r.ReadUShort();
            if (count == 0)
                return string.Empty;
            var bytes = new byte[count];
            for (int i = 0; i < count; i++)
                bytes[i] = r.ReadByte();
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }
}
