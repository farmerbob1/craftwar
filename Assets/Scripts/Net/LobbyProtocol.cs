using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>
    /// Pre-game-only seat state. Distinct from Sim's <see cref="Controller"/>
    /// (None/Human/Computer): the lobby needs a fourth state a running match
    /// can never be in — a playable seat waiting on a human, not yet resolved
    /// to anything. An empty seat defaults here, not to Computer: AI presence
    /// must be the host's deliberate choice. Converted to Controller only at
    /// StartMatch, and only once no seat is still Open (see
    /// LobbyPayload.HasOpenSeats).
    /// </summary>
    public enum LobbySeatStatus : byte
    {
        /// <summary>Not part of the match — either the map never had this
        /// seat, or the host removed it.</summary>
        Closed = 0,
        /// <summary>Playable, waiting for a human to claim it.</summary>
        Open,
        Computer,
        Human,
    }

    /// <summary>One seat as the lobby sees it.</summary>
    public struct LobbySlot
    {
        public byte SeatStatus;   // Craftwar.Net.LobbySeatStatus
        public byte Race;
        public byte Team;
        public byte AiTier;
        public string Name;
    }

    /// <summary>
    /// Everything the host has decided about the match, mirrored to every client.
    ///
    /// Defined here rather than reusing <c>MatchConfig</c> because the app layer
    /// references the net layer and not the other way round — and because what
    /// goes on the wire should be an explicit, versioned shape rather than
    /// whatever a serializable class happens to contain today.
    /// </summary>
    public sealed class LobbyPayload
    {
        public string MapPath = "";
        public ulong Seed = 42;
        public byte TicksPerTurn = SimConstants.TicksPerCommandTurn;
        public byte InputDelayTurns = 2;
        public readonly LobbySlot[] Slots = new LobbySlot[SimConstants.MaxPlayers];

        public void Write(ref ByteWriter w)
        {
            NetMessages.WriteString(ref w, MapPath);
            w.WriteULong(Seed);
            w.WriteByte(TicksPerTurn);
            w.WriteByte(InputDelayTurns);
            for (int i = 0; i < Slots.Length; i++)
            {
                ref LobbySlot s = ref Slots[i];
                w.WriteByte(s.SeatStatus);
                w.WriteByte(s.Race);
                w.WriteByte(s.Team);
                w.WriteByte(s.AiTier);
                NetMessages.WriteString(ref w, s.Name ?? "");
            }
        }

        public static LobbyPayload Read(ref ByteReader r)
        {
            var payload = new LobbyPayload
            {
                MapPath = NetMessages.ReadString(ref r),
                Seed = r.ReadULong(),
                TicksPerTurn = r.ReadByte(),
                InputDelayTurns = r.ReadByte(),
            };
            for (int i = 0; i < payload.Slots.Length; i++)
            {
                payload.Slots[i] = new LobbySlot
                {
                    SeatStatus = r.ReadByte(),
                    Race = r.ReadByte(),
                    Team = r.ReadByte(),
                    AiTier = r.ReadByte(),
                    Name = NetMessages.ReadString(ref r),
                };
            }
            return payload;
        }

        /// <summary>Seats a turn must wait for: everyone actually playing,
        /// human or computer. A computer seat still participates — the host
        /// produces its input.</summary>
        public byte[] ParticipatingSlots()
        {
            int count = 0;
            for (int i = 0; i < Slots.Length; i++)
                if (Plays(Slots[i].SeatStatus))
                    count++;

            var result = new byte[count];
            int n = 0;
            for (byte i = 0; i < Slots.Length; i++)
                if (Plays(Slots[i].SeatStatus))
                    result[n++] = i;
            return result;
        }

        static bool Plays(byte status) =>
            status == (byte)LobbySeatStatus.Human || status == (byte)LobbySeatStatus.Computer;

        /// <summary>Lowest seat waiting for a human, or -1.</summary>
        public int FirstOpenSeat()
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].SeatStatus == (byte)LobbySeatStatus.Open)
                    return i;
            return -1;
        }

        /// <summary>True while any playable seat is still unresolved — the
        /// match cannot start until the host closes it or gives it to the
        /// computer, so AI presence is always a deliberate choice.</summary>
        public bool HasOpenSeats()
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].SeatStatus == (byte)LobbySeatStatus.Open)
                    return true;
            return false;
        }

        public int HumanCount()
        {
            int n = 0;
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].SeatStatus == (byte)LobbySeatStatus.Human)
                    n++;
            return n;
        }

        /// <summary>Seats that will actually be in the match once resolved —
        /// closed seats excluded, Open seats included (they are waiting, not
        /// absent).</summary>
        public int PlayableCount()
        {
            int n = 0;
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].SeatStatus != (byte)LobbySeatStatus.Closed)
                    n++;
            return n;
        }
    }
}
