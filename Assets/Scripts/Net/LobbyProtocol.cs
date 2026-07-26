using Craftwar.Sim;

namespace Craftwar.Net
{
    /// <summary>One seat as the lobby sees it.</summary>
    public struct LobbySlot
    {
        public byte Controller;   // Craftwar.Sim.Controller
        public byte Race;
        public byte Team;
        public byte AiTier;
        /// <summary>Taken by a person (the host, or a connected client).</summary>
        public bool Human;
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
                w.WriteByte(s.Controller);
                w.WriteByte(s.Race);
                w.WriteByte(s.Team);
                w.WriteByte(s.AiTier);
                w.WriteByte((byte)(s.Human ? 1 : 0));
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
                    Controller = r.ReadByte(),
                    Race = r.ReadByte(),
                    Team = r.ReadByte(),
                    AiTier = r.ReadByte(),
                    Human = r.ReadByte() != 0,
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
                if (Slots[i].Controller != (byte)Controller.None)
                    count++;

            var result = new byte[count];
            int n = 0;
            for (byte i = 0; i < Slots.Length; i++)
                if (Slots[i].Controller != (byte)Controller.None)
                    result[n++] = i;
            return result;
        }

        /// <summary>Lowest playable seat not yet claimed by a person, or -1.</summary>
        public int FirstFreeHumanSeat()
        {
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].Controller != (byte)Controller.None && !Slots[i].Human)
                    return i;
            return -1;
        }

        public int HumanCount()
        {
            int n = 0;
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].Human)
                    n++;
            return n;
        }

        public int PlayableCount()
        {
            int n = 0;
            for (int i = 0; i < Slots.Length; i++)
                if (Slots[i].Controller != (byte)Controller.None)
                    n++;
            return n;
        }
    }
}
