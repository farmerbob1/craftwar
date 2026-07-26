namespace Craftwar.Sim
{
    /// <summary>
    /// A complete, self-contained snapshot of a running match.
    ///
    /// Powers the pause menu's Save button and the reconnect handover. The bar is
    /// higher than "the game looks right after loading": a loaded sim must be
    /// BIT-IDENTICAL to the live one, because a reconnecting player rejoins a
    /// match already in progress and any difference is an immediate desync.
    ///
    /// Self-contained by choice — the tile layer, terrain planes and the resolved
    /// rule tables are all written out. Saves then survive the player moving or
    /// uninstalling Warcraft II, which matters because most map paths resolve
    /// into that install, and it removes a whole class of "the map changed under
    /// the save" bugs.
    ///
    /// What is NOT written, because it is exactly reconstructible:
    ///   - TerrainMap clearance and region labels (pure functions of passability)
    ///   - Visible and Detected (TickFog rebuilds both from scratch each tick)
    ///   - every running checksum (recomputed as the state is installed)
    ///   - the pathfinder (its stamp/generation scheme makes a cold instance
    ///     behave identically to a warm one)
    /// Everything else is written, including things that look derivable but are
    /// not: the free-slot list, dead units' generation counters, path contents,
    /// and both occupancy layers.
    /// </summary>
    public static class SimSerializer
    {
        public const uint Magic = 0x56535743; // "CWSV" little-endian
        public const ushort Version = 1;

        public static byte[] Save(GameSim sim)
        {
            var w = new ByteWriter(1 << 16);
            var s = sim.State;

            w.WriteUInt(Magic);
            w.WriteUShort(Version);
            w.WriteUInt(SimConstants.SimVersion);

            // --- clock and randomness ---
            w.WriteInt(s.Tick);
            // Never re-derive the RNG from the seed: NextUInt(bound) uses
            // rejection sampling, so how many draws a given tick consumed is
            // data-dependent and cannot be replayed by counting.
            w.WriteULong(s.Rng.State);
            w.WriteULong(s.Rng.Inc);

            // --- rules ---
            WriteRules(ref w, s.Rules);

            // --- terrain ---
            var terrain = s.Terrain;
            w.WriteInt(terrain.Width);
            w.WriteInt(terrain.Height);
            WriteBytesRle(ref w, terrain.PassablePlane);
            WriteBytesRle(ref w, terrain.WoodPlane);
            WriteBytesRle(ref w, terrain.ShorePlane);

            // --- tiles ---
            WriteUShortsRle(ref w, s.TileArray);

            // --- players ---
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
                WritePlayer(ref w, in s.Players[p]);
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
                w.WriteInt(s.LastUnderAttackTick[p]);

            // --- units ---
            // ALL slots up to HighestUnitIndex, not just the living ones: a dead
            // slot's Gen decides the id of the next unit spawned into it, so
            // dropping it would make the first post-load spawn produce a
            // different UnitId than a live peer's.
            w.WriteInt(s.HighestUnitIndex);
            for (int i = 0; i < s.HighestUnitIndex; i++)
                WriteUnit(ref w, in s.Units[i]);

            w.WriteInt(s.FreeCount);
            for (int i = 0; i < s.FreeCount; i++)
                w.WriteUShort(s.FreeSlotAt(i));

            // --- paths ---
            // Only PathLength entries matter; the tail of an over-allocated array
            // is never read. PathCursor/PathLength are already inside Unit.
            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                ushort[] path = s.UnitPaths[i];
                int length = path == null ? 0 : s.Units[i].PathLength;
                if (length > (path?.Length ?? 0))
                    length = path?.Length ?? 0;
                w.WriteUShort((ushort)length);
                for (int n = 0; n < length; n++)
                    w.WriteUShort(path[n]);
            }

            // --- projectiles ---
            w.WriteInt(s.Projectiles.Length);
            for (int i = 0; i < s.Projectiles.Length; i++)
            {
                ref Projectile p = ref s.Projectiles[i];
                w.WriteByte((byte)(p.Active ? 1 : 0));
                if (!p.Active)
                    continue;
                w.WriteByte(p.MissileType);
                w.WriteInt(p.PixX);
                w.WriteInt(p.PixY);
                w.WriteUInt(p.TargetUnit);
                w.WriteInt(p.Damage);
                w.WriteByte(p.SourcePlayer);
            }

            // --- occupancy ---
            // Written rather than rebuilt. Occupy overwrites unconditionally
            // while Vacate only clears cells matching its own id, so the layer is
            // a function of write history, not of where units currently stand:
            // two units can legitimately share a tile, and a cell can read empty
            // with somebody on it. A rebuild cannot reproduce either.
            WriteUIntsRle(ref w, s.OccupancySurface);
            WriteUIntsRle(ref w, s.OccupancyAir);

            // --- explored fog ---
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                byte[] grid = s.Explored?[p];
                w.WriteByte((byte)(grid == null ? 0 : 1));
                if (grid != null)
                    WriteBytesRle(ref w, grid);
            }

            return w.ToArray();
        }

        public static GameSim Load(byte[] data)
        {
            var r = new ByteReader(data);
            if (r.ReadUInt() != Magic)
                throw new System.IO.InvalidDataException("Not a Craftwar save");
            ushort version = r.ReadUShort();
            if (version != Version)
                throw new System.IO.InvalidDataException($"Unsupported save version {version}");
            uint simVersion = r.ReadUInt();
            if (simVersion != SimConstants.SimVersion)
                throw new System.IO.InvalidDataException(
                    $"Save was written by simulation version {simVersion}, this build is " +
                    $"{SimConstants.SimVersion}. Loading it would not reproduce the same game.");

            int tick = r.ReadInt();
            ulong rngState = r.ReadULong();
            ulong rngInc = r.ReadULong();

            var rules = ReadRules(ref r);

            int width = r.ReadInt();
            int height = r.ReadInt();
            byte[] passable = ReadBytesRle(ref r);
            byte[] wood = ReadBytesRle(ref r);
            byte[] shore = ReadBytesRle(ref r);
            var terrain = TerrainMap.FromPlanes(width, height, passable, wood, shore);

            ushort[] tiles = ReadUShortsRle(ref r);

            // Bypass the constructor's two RNG draws by assigning the stream
            // directly; the saved state already accounts for them.
            var sim = new GameSim(0);
            var s = sim.State;
            s.Rng = new Pcg32 { State = rngState, Inc = rngInc };
            s.Tick = tick;
            s.Rules = rules;
            s.Terrain = terrain;
            s.InstallTiles(tiles);
            s.OccupancySurface = new uint[width * height];
            s.OccupancyAir = new uint[width * height];
            s.Visible = new byte[SimConstants.MaxPlayers][];
            s.Explored = new byte[SimConstants.MaxPlayers][];
            s.Detected = new byte[SimConstants.MaxPlayers][];

            for (int p = 0; p < SimConstants.MaxPlayers; p++)
                s.Players[p] = ReadPlayer(ref r);
            for (int p = 0; p < SimConstants.MaxPlayers; p++)
                s.LastUnderAttackTick[p] = r.ReadInt();

            s.HighestUnitIndex = r.ReadInt();
            for (int i = 0; i < s.HighestUnitIndex; i++)
                s.Units[i] = ReadUnit(ref r);

            int freeCount = r.ReadInt();
            var freeSlots = new ushort[freeCount];
            for (int i = 0; i < freeCount; i++)
                freeSlots[i] = r.ReadUShort();
            s.RestoreFreeList(freeSlots, freeCount);

            for (int i = 0; i < s.HighestUnitIndex; i++)
            {
                int length = r.ReadUShort();
                if (length == 0)
                {
                    s.UnitPaths[i] = null;
                    continue;
                }
                var path = new ushort[length];
                for (int n = 0; n < length; n++)
                    path[n] = r.ReadUShort();
                s.UnitPaths[i] = path;
            }

            int projectileCount = r.ReadInt();
            for (int i = 0; i < projectileCount && i < s.Projectiles.Length; i++)
            {
                bool active = r.ReadByte() != 0;
                if (!active)
                {
                    s.Projectiles[i] = default;
                    continue;
                }
                s.Projectiles[i] = new Projectile
                {
                    Active = true,
                    MissileType = r.ReadByte(),
                    PixX = r.ReadInt(),
                    PixY = r.ReadInt(),
                    TargetUnit = r.ReadUInt(),
                    Damage = r.ReadInt(),
                    SourcePlayer = r.ReadByte(),
                };
            }

            CopyInto(ReadUIntsRle(ref r), s.OccupancySurface);
            CopyInto(ReadUIntsRle(ref r), s.OccupancyAir);

            for (int p = 0; p < SimConstants.MaxPlayers; p++)
            {
                bool present = r.ReadByte() != 0;
                s.Explored[p] = present ? ReadBytesRle(ref r) : null;
                if (present)
                {
                    // Visible and Detected are rebuilt by the first TickFog, but
                    // they must exist for the slots that had them.
                    s.Visible[p] = new byte[width * height];
                    s.Detected[p] = new byte[width * height];
                }
            }

            // The grids above were installed wholesale, bypassing the funnels
            // that normally keep the running checksums exact.
            s.ReseedChecksums();

            sim.AdoptLoadedState(width, height);
            return sim;
        }

        static void CopyInto(uint[] source, uint[] destination)
        {
            int n = source.Length < destination.Length ? source.Length : destination.Length;
            System.Array.Copy(source, destination, n);
        }

        // --- Rules ---------------------------------------------------------------

        static void WriteRules(ref ByteWriter w, RuleSet rules)
        {
            w.WriteInt(rules.Units.Length);
            for (int i = 0; i < rules.Units.Length; i++)
            {
                ref UnitTypeData u = ref rules.Units[i];
                w.WriteInt(u.Sight); w.WriteInt(u.Hp);
                w.WriteByte((byte)(u.HasMagic ? 1 : 0));
                w.WriteInt(u.BuildTime); w.WriteInt(u.GoldCost);
                w.WriteInt(u.LumberCost); w.WriteInt(u.OilCost);
                w.WriteInt(u.SizeW); w.WriteInt(u.SizeH);
                w.WriteInt(u.BoxW); w.WriteInt(u.BoxH);
                w.WriteInt(u.AttackRange);
                w.WriteInt(u.ReactRangeComputer); w.WriteInt(u.ReactRangeHuman);
                w.WriteInt(u.Armor);
                w.WriteByte((byte)(u.RectSelectable ? 1 : 0));
                w.WriteInt(u.Priority);
                w.WriteInt(u.BasicDamage); w.WriteInt(u.PiercingDamage);
                w.WriteByte((byte)(u.WeaponsUpgradable ? 1 : 0));
                w.WriteByte((byte)(u.ArmorUpgradable ? 1 : 0));
                w.WriteByte(u.MissileWeapon); w.WriteByte(u.MoveDomain);
                w.WriteInt(u.DecayRate); w.WriteInt(u.Annoy);
                w.WriteByte(u.RightClickAction);
                w.WriteInt(u.PointValue); w.WriteByte(u.CanTarget);
                w.WriteUInt((uint)u.Flags);
            }
            w.WriteInt(rules.Upgrades.Length);
            for (int i = 0; i < rules.Upgrades.Length; i++)
            {
                ref UpgradeData u = ref rules.Upgrades[i];
                w.WriteInt(u.Time); w.WriteInt(u.Gold); w.WriteInt(u.Lumber);
                w.WriteInt(u.Oil); w.WriteInt(u.Icon); w.WriteInt(u.Group);
                w.WriteUInt(u.Flags);
            }
        }

        static RuleSet ReadRules(ref ByteReader r)
        {
            var rules = RuleSet.CreateDefault();
            int unitCount = r.ReadInt();
            for (int i = 0; i < unitCount; i++)
            {
                var u = new UnitTypeData
                {
                    Sight = r.ReadInt(), Hp = r.ReadInt(),
                    HasMagic = r.ReadByte() != 0,
                    BuildTime = r.ReadInt(), GoldCost = r.ReadInt(),
                    LumberCost = r.ReadInt(), OilCost = r.ReadInt(),
                    SizeW = r.ReadInt(), SizeH = r.ReadInt(),
                    BoxW = r.ReadInt(), BoxH = r.ReadInt(),
                    AttackRange = r.ReadInt(),
                    ReactRangeComputer = r.ReadInt(), ReactRangeHuman = r.ReadInt(),
                    Armor = r.ReadInt(),
                    RectSelectable = r.ReadByte() != 0,
                    Priority = r.ReadInt(),
                    BasicDamage = r.ReadInt(), PiercingDamage = r.ReadInt(),
                    WeaponsUpgradable = r.ReadByte() != 0,
                    ArmorUpgradable = r.ReadByte() != 0,
                    MissileWeapon = r.ReadByte(), MoveDomain = r.ReadByte(),
                    DecayRate = r.ReadInt(), Annoy = r.ReadInt(),
                    RightClickAction = r.ReadByte(),
                    PointValue = r.ReadInt(), CanTarget = r.ReadByte(),
                    Flags = (UnitTypeFlags)r.ReadUInt(),
                };
                if (i < rules.Units.Length)
                    rules.Units[i] = u;
            }
            int upgradeCount = r.ReadInt();
            for (int i = 0; i < upgradeCount; i++)
            {
                var u = new UpgradeData
                {
                    Time = r.ReadInt(), Gold = r.ReadInt(), Lumber = r.ReadInt(),
                    Oil = r.ReadInt(), Icon = r.ReadInt(), Group = r.ReadInt(),
                    Flags = r.ReadUInt(),
                };
                if (i < rules.Upgrades.Length)
                    rules.Upgrades[i] = u;
            }
            return rules;
        }

        // --- Structs -------------------------------------------------------------

        // Field order mirrors HashInto exactly, so "what is hashed" and "what is
        // saved" cannot silently drift apart.

        static void WritePlayer(ref ByteWriter w, in PlayerState p)
        {
            w.WriteByte((byte)(p.InGame ? 1 : 0));
            w.WriteByte((byte)p.Race);
            w.WriteByte((byte)p.Controller);
            w.WriteByte(p.Team);
            w.WriteByte((byte)p.Outcome);
            w.WriteInt(p.Gold); w.WriteInt(p.Lumber); w.WriteInt(p.Oil);
            w.WriteInt(p.FoodUsed); w.WriteInt(p.FoodMax);
            w.WriteULong(p.Researched);
            w.WriteUInt(p.AllowedUnits); w.WriteUInt(p.AllowedUpgrades);
            w.WriteUInt(p.AllowedSpells);
            w.WriteInt(p.HarvestBonusTenths); w.WriteInt(p.SightBonus);
            w.WriteInt(p.GoldGathered); w.WriteInt(p.LumberGathered);
            w.WriteInt(p.OilGathered);
            w.WriteInt(p.UnitsKilled); w.WriteInt(p.BuildingsRazed);
            w.WriteInt(p.UnitsLost); w.WriteInt(p.BuildingsLost);
        }

        static PlayerState ReadPlayer(ref ByteReader r) => new PlayerState
        {
            InGame = r.ReadByte() != 0,
            Race = (Race)r.ReadByte(),
            Controller = (Controller)r.ReadByte(),
            Team = r.ReadByte(),
            Outcome = (PlayerOutcome)r.ReadByte(),
            Gold = r.ReadInt(), Lumber = r.ReadInt(), Oil = r.ReadInt(),
            FoodUsed = r.ReadInt(), FoodMax = r.ReadInt(),
            Researched = r.ReadULong(),
            AllowedUnits = r.ReadUInt(), AllowedUpgrades = r.ReadUInt(),
            AllowedSpells = r.ReadUInt(),
            HarvestBonusTenths = r.ReadInt(), SightBonus = r.ReadInt(),
            GoldGathered = r.ReadInt(), LumberGathered = r.ReadInt(),
            OilGathered = r.ReadInt(),
            UnitsKilled = r.ReadInt(), BuildingsRazed = r.ReadInt(),
            UnitsLost = r.ReadInt(), BuildingsLost = r.ReadInt(),
        };

        static void WriteUnit(ref ByteWriter w, in Unit u)
        {
            w.WriteUShort(u.Gen);
            w.WriteUShort((ushort)u.Flags);
            w.WriteUShort(u.TypeId);
            w.WriteByte(u.Player);
            w.WriteByte(u.Facing);
            w.WriteUShort(u.TileX); w.WriteUShort(u.TileY);
            w.WriteInt(u.PixX); w.WriteInt(u.PixY);
            w.WriteInt(u.Hp);
            w.WriteByte((byte)u.Order);
            w.WriteUShort(u.OrderX); w.WriteUShort(u.OrderY);
            w.WriteUShort(u.PathCursor); w.WriteUShort(u.PathLength);
            w.WriteInt(u.MoveAccum);
            w.WriteByte(u.StepRemaining);
            w.WriteByte((byte)u.StepDX); w.WriteByte((byte)u.StepDY);
            w.WriteByte(u.WaitTicks);
            w.WriteUInt(u.AttackTarget);
            w.WriteByte(u.Cooldown);
            w.WriteUShort(u.ChaseX); w.WriteUShort(u.ChaseY);
            w.WriteUShort(u.GoalX); w.WriteUShort(u.GoalY);
            w.WriteByte((byte)u.Harvest);
            w.WriteByte((byte)u.Carry);
            w.WriteUShort(u.Timer);
            w.WriteUInt(u.ResourceTarget);
            w.WriteInt(u.ResourceAmount);
            w.WriteUShort(u.BuildType);
            w.WriteUShort(u.TrainTicks);
            w.WriteByte(u.ResearchId);
            w.WriteUShort(u.RallyX); w.WriteUShort(u.RallyY);
            w.WriteUInt(u.Transport);
            w.WriteByte(u.CargoCount);
        }

        static Unit ReadUnit(ref ByteReader r) => new Unit
        {
            Gen = r.ReadUShort(),
            Flags = (UnitFlags)r.ReadUShort(),
            TypeId = r.ReadUShort(),
            Player = r.ReadByte(),
            Facing = r.ReadByte(),
            TileX = r.ReadUShort(), TileY = r.ReadUShort(),
            PixX = r.ReadInt(), PixY = r.ReadInt(),
            Hp = r.ReadInt(),
            Order = (OrderType)r.ReadByte(),
            OrderX = r.ReadUShort(), OrderY = r.ReadUShort(),
            PathCursor = r.ReadUShort(), PathLength = r.ReadUShort(),
            MoveAccum = r.ReadInt(),
            StepRemaining = r.ReadByte(),
            StepDX = (sbyte)r.ReadByte(), StepDY = (sbyte)r.ReadByte(),
            WaitTicks = r.ReadByte(),
            AttackTarget = r.ReadUInt(),
            Cooldown = r.ReadByte(),
            ChaseX = r.ReadUShort(), ChaseY = r.ReadUShort(),
            GoalX = r.ReadUShort(), GoalY = r.ReadUShort(),
            Harvest = (HarvestStage)r.ReadByte(),
            Carry = (CarryType)r.ReadByte(),
            Timer = r.ReadUShort(),
            ResourceTarget = r.ReadUInt(),
            ResourceAmount = r.ReadInt(),
            BuildType = r.ReadUShort(),
            TrainTicks = r.ReadUShort(),
            ResearchId = r.ReadByte(),
            RallyX = r.ReadUShort(), RallyY = r.ReadUShort(),
            Transport = r.ReadUInt(),
            CargoCount = r.ReadByte(),
        };

        // --- Run-length coding ---------------------------------------------------
        //
        // The grids are overwhelmingly uniform: occupancy is nearly all zeros,
        // explored is large blocks of 0 and 1, terrain planes are long runs. A
        // 128x128 8-player save drops from well over 300 KB to a few KB.

        static void WriteBytesRle(ref ByteWriter w, byte[] data)
        {
            w.WriteInt(data.Length);
            int i = 0;
            while (i < data.Length)
            {
                byte value = data[i];
                int run = 1;
                while (i + run < data.Length && data[i + run] == value && run < ushort.MaxValue)
                    run++;
                w.WriteUShort((ushort)run);
                w.WriteByte(value);
                i += run;
            }
        }

        static byte[] ReadBytesRle(ref ByteReader r)
        {
            int length = r.ReadInt();
            var data = new byte[length];
            int i = 0;
            while (i < length)
            {
                int run = r.ReadUShort();
                byte value = r.ReadByte();
                if (run <= 0 || i + run > length)
                    throw new System.IO.InvalidDataException("Corrupt run in save data");
                for (int n = 0; n < run; n++)
                    data[i + n] = value;
                i += run;
            }
            return data;
        }

        static void WriteUShortsRle(ref ByteWriter w, ushort[] data)
        {
            w.WriteInt(data.Length);
            int i = 0;
            while (i < data.Length)
            {
                ushort value = data[i];
                int run = 1;
                while (i + run < data.Length && data[i + run] == value && run < ushort.MaxValue)
                    run++;
                w.WriteUShort((ushort)run);
                w.WriteUShort(value);
                i += run;
            }
        }

        static ushort[] ReadUShortsRle(ref ByteReader r)
        {
            int length = r.ReadInt();
            var data = new ushort[length];
            int i = 0;
            while (i < length)
            {
                int run = r.ReadUShort();
                ushort value = r.ReadUShort();
                if (run <= 0 || i + run > length)
                    throw new System.IO.InvalidDataException("Corrupt run in save data");
                for (int n = 0; n < run; n++)
                    data[i + n] = value;
                i += run;
            }
            return data;
        }

        static void WriteUIntsRle(ref ByteWriter w, uint[] data)
        {
            w.WriteInt(data.Length);
            int i = 0;
            while (i < data.Length)
            {
                uint value = data[i];
                int run = 1;
                while (i + run < data.Length && data[i + run] == value && run < ushort.MaxValue)
                    run++;
                w.WriteUShort((ushort)run);
                w.WriteUInt(value);
                i += run;
            }
        }

        static uint[] ReadUIntsRle(ref ByteReader r)
        {
            int length = r.ReadInt();
            var data = new uint[length];
            int i = 0;
            while (i < length)
            {
                int run = r.ReadUShort();
                uint value = r.ReadUInt();
                if (run <= 0 || i + run > length)
                    throw new System.IO.InvalidDataException("Corrupt run in save data");
                for (int n = 0; n < run; n++)
                    data[i + n] = value;
                i += run;
            }
            return data;
        }
    }
}
