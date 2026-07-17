namespace Craftwar.Sim
{
    public enum Race : byte
    {
        Human = 0,
        Orc = 1,
        Neutral = 2,
    }

    /// <summary>Per-player economy and status. Index = player slot 0-7.</summary>
    public struct PlayerState
    {
        public bool InGame;
        public Race Race;
        public int Gold;
        public int Lumber;
        public int Oil;
        public int FoodUsed;
        public int FoodMax;

        public void HashInto(ref StateHash h)
        {
            h.Add((byte)(InGame ? 1 : 0));
            h.Add((byte)Race);
            h.Add(Gold);
            h.Add(Lumber);
            h.Add(Oil);
            h.Add(FoodUsed);
            h.Add(FoodMax);
        }
    }
}
