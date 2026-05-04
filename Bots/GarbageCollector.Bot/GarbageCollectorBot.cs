using System.IO;
using TankDestroyer.API;

namespace GarbageCollector.Bot;

[Bot("Garbage Collector", "DJ RuSCH", "FF0000")]
public class GarbageCollectorBot : IPlayerBot
{
    private static readonly Direction[] Directions =
        [Direction.North, Direction.East, Direction.South, Direction.West];

    private static readonly TurretDirection[] TurretDirections =
    [
        TurretDirection.North, TurretDirection.East, TurretDirection.South, TurretDirection.West,
        TurretDirection.NorthEast, TurretDirection.NorthWest, TurretDirection.SouthEast, TurretDirection.SouthWest
    ];

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "garbagecollector.log");

    private static readonly StreamWriter? LogWriter = InitLog();

    private readonly Dictionary<int, EnemyHistory> _enemyHistory = [];
    private readonly List<PendingBullet> _myPendingBullets = [];

    // Track the last 8 positions to penalise oscillation and detect longer cycles.
    // 8 slots means a 4-tile cycle (A→B→C→D→A) no longer fills the whole window,
    // so the oldest entries start falling off and break symmetry.
    private readonly Queue<(int X, int Y)> _recentPositions = new(8);

    // Loop detection: count turns we've spent circling a small area
    private int _loopTurns = 0;
    private readonly HashSet<(int X, int Y)> _loopWindow = new();

    private int _mapWidth;
    private int _mapHeight;

    private static int _matchNumber = 0;
    private int? _lastSeenMyHealth = null;
    private int _previousTurnEnemyCount = 0;

    private record EnemyHistory(int X, int Y, int Health, TurretDirection Turret, int StationaryTurns);
    private record PendingBullet(int OriginX, int OriginY, TurretDirection Direction, int FiredOnTurn, int TurretId, int ExpiresOnTurn);

    private int _turnNumber = 0;
    private int _campingTurns = 0;
    private (int X, int Y)? _campingPosition = null;

    private static StreamWriter? InitLog()
    {
#if DEBUG
        bool fileExists = File.Exists(LogPath);
        var writer = new StreamWriter(LogPath, append: true) { AutoFlush = true };
        if (fileExists)
        {
            writer.WriteLine();
        }

        writer.WriteLine($"╔══════════════════════════════════════════════════════════════╗");
        writer.WriteLine($"║ Process started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}                       ║");
        writer.WriteLine($"║ Log file: {LogPath,-50} ║");
        writer.WriteLine($"╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine($"[GarbageCollector] Log file: {LogPath}");
        return writer;
#else
        return null;
#endif
    }

    private static void Log(string message)
    {
#if DEBUG
        LogWriter?.WriteLine(message);
#endif
    }

    private static void LogMatchStart(int matchNumber)
    {
#if DEBUG
        if (LogWriter is null)
        {
            return;
        }

        string title = $" MATCH #{matchNumber} starting at {DateTime.Now:HH:mm:ss}";
        LogWriter.WriteLine();
        LogWriter.WriteLine($"┌──────────────────────────────────────────────────────────────┐");
        LogWriter.WriteLine($"│{title.PadRight(62)}│");
        LogWriter.WriteLine($"└──────────────────────────────────────────────────────────────┘");
#endif
    }

    public void DoTurn(ITurnContext ctx)
    {
        _mapWidth = ctx.GetMapWidth();
        _mapHeight = ctx.GetMapHeight();

        var me = ctx.Tank;
        var allTanks = ctx.GetTanks();
        var enemies = GetEnemies(allTanks, me.OwnerId);
        var bullets = ctx.GetBullets();

        DetectMatchStart(me, enemies, bullets);
        _turnNumber++;

        PruneStalePendingBullets(enemies);

        var myTileType = GetTile(ctx, me.X, me.Y).TileType;
        Log("");
        Log($"[T{_turnNumber}] me=({me.X},{me.Y})/{myTileType} hp={me.Health} turret={me.TurretDirection} | enemies={enemies.Length} | pending={_myPendingBullets.Count}");

        if (enemies.Length == 0)
        {
            Log("  no enemies — repositioning");
            TryReposition(ctx, me);
            EndTurn(me, enemies);
            return;
        }

        var target = PickTarget(ctx, me, enemies);
        foreach (var e in enemies)
        {
            Log($"  {(e.OwnerId == target.OwnerId ? "target" : "enemy ")}=({e.X},{e.Y}) hp={e.Health}");
        }

        var threats = AnalyseThreats(ctx, me, enemies, bullets);
        Log($"  threats: incomingBullets={threats.IncomingBullets.Count} dangerTiles={threats.DangerTiles.Count}");

        var move = ChooseBestMove(ctx, me, target, enemies, bullets, threats);
        Log($"  >>> chose move={move.Direction} score={move.Score} reason={move.Reason}");

        var (postX, postY) = move.Direction.HasValue
            ? ApplyDirection(me.X, me.Y, move.Direction.Value)
            : (me.X, me.Y);

        if (move.Direction.HasValue)
        {
            ctx.MoveTank(move.Direction.Value);
        }

        var fireDecision = DecideFiring(ctx, postX, postY, target, enemies);
        if (fireDecision != null)
        {
            Log($"  >>> firing turret={fireDecision.Direction} at ({fireDecision.ActualTarget.X},{fireDecision.ActualTarget.Y})");
            ctx.RotateTurret(fireDecision.Direction);
            ctx.Fire();
            RegisterMyShot(postX, postY, fireDecision.Direction, fireDecision.ActualTarget);
        }
        else
        {
            var rotateAim = TurretDirectionTo(postX, postY, target.X, target.Y);
            if (rotateAim.HasValue)
            {
                ctx.RotateTurret(rotateAim.Value);
            }
        }

        EndTurn(me, enemies);
    }

    private void DetectMatchStart(ITank me, ITank[] enemies, IBullet[] bullets)
    {
        bool allFullHealth = me.Health == 100 && enemies.All(e => e.Health == 100);
        bool noBullets = bullets.Length == 0;
        bool firstEverCall = _matchNumber == 0;

        bool myHealthJumpedUp = _lastSeenMyHealth.HasValue
                              && me.Health > _lastSeenMyHealth.Value
                              && me.Health == 100;

        // An enemy count *increase* can only mean a new match (dead tanks don't revive).
        // A decrease just means a kill mid-match — don't reset on a kill.
        bool enemyCountIncreased = _previousTurnEnemyCount > 0
                                && enemies.Length > _previousTurnEnemyCount;

        bool looksLikeFreshMatch = allFullHealth && noBullets;

        if (firstEverCall || (looksLikeFreshMatch && (myHealthJumpedUp || enemyCountIncreased)))
        {
            _matchNumber++;
            _turnNumber = 0;
            _enemyHistory.Clear();
            _myPendingBullets.Clear();
            _recentPositions.Clear();
            _campingTurns = 0;
            _campingPosition = null;
            _loopTurns = 0;
            _loopWindow.Clear();
            LogMatchStart(_matchNumber);
        }

        _lastSeenMyHealth = me.Health;
        _previousTurnEnemyCount = enemies.Length;
    }

    // ─── Threat analysis ──────────────────────────────────────────────────────

    private record ThreatMap(List<IBullet> IncomingBullets, HashSet<(int X, int Y)> DangerTiles);

    private ThreatMap AnalyseThreats(ITurnContext ctx, ITank me, ITank[] enemies, IBullet[] bullets)
    {
        var incoming = new List<IBullet>();
        var dangerTiles = new HashSet<(int X, int Y)>();

        foreach (var b in bullets)
        {
            if (BulletIsMine(b))
            {
                continue;
            }

            var (dx, dy) = TurretDelta(b.Direction);
            int bx = b.X, by = b.Y;
            for (int step = 0; step <= 6; step++)
            {
                if ((uint)bx >= (uint)_mapWidth || (uint)by >= (uint)_mapHeight)
                {
                    break;
                }

                dangerTiles.Add((bx, by));

                if (bx == me.X && by == me.Y)
                {
                    incoming.Add(b);
                }

                var t = GetTile(ctx, bx, by).TileType;
                if (step > 0 && (t == TileType.Tree || t == TileType.Building))
                {
                    break;
                }

                bx += dx;
                by += dy;
            }
        }

        foreach (var enemy in enemies)
        {
            // Only mark tiles along the enemy's current facing direction and the two
            // immediate neighbours (±1 step in the 8-direction ring). Projecting all
            // 8 directions inflates danger tiles massively and makes nearly every
            // tile look unsafe, freezing movement unnecessarily.
            var facingDirs = AdjacentTurretDirections(enemy.TurretDirection);
            foreach (var td in facingDirs)
            {
                var (dx, dy) = TurretDelta(td);
                int ex = enemy.X, ey = enemy.Y;
                for (int step = 1; step <= 6; step++)
                {
                    ex += dx;
                    ey += dy;
                    if ((uint)ex >= (uint)_mapWidth || (uint)ey >= (uint)_mapHeight)
                    {
                        break;
                    }

                    var t = GetTile(ctx, ex, ey).TileType;
                    if (t == TileType.Tree || t == TileType.Building)
                    {
                        break;
                    }

                    dangerTiles.Add((ex, ey));
                }
            }
        }

        return new ThreatMap(incoming, dangerTiles);
    }

    private bool BulletIsMine(IBullet b)
    {
        foreach (var p in _myPendingBullets)
        {
            var (dx, dy) = TurretDelta(p.Direction);
            int turnsSince = _turnNumber - p.FiredOnTurn;
            int expectedX = p.OriginX + dx * 6 * turnsSince;
            int expectedY = p.OriginY + dy * 6 * turnsSince;
            if (Math.Abs(b.X - expectedX) <= 6 && Math.Abs(b.Y - expectedY) <= 6 && b.Direction == p.Direction)
            {
                return true;
            }
        }

        return false;
    }

    // ─── Move scoring ─────────────────────────────────────────────────────────

    private record MoveChoice(Direction? Direction, int Score, string Reason);

    private MoveChoice ChooseBestMove(ITurnContext ctx, ITank me, ITank target,
        ITank[] enemies, IBullet[] bullets, ThreatMap threats)
    {
        // "Can I shoot from here?" should be true if we can hit *any* enemy,
        // not just the primary target. Otherwise we incorrectly trigger anti-camp
        // logic when we're actually holding a good multi-enemy firing position.
        bool canFireFromHere = CanFireAtAny(ctx, me.X, me.Y, enemies)
                            && GetTile(ctx, me.X, me.Y).TileType != TileType.Tree;

        bool isCamping = _campingTurns >= 4 && !canFireFromHere;

        // Loop escape: if we've been cycling the same ≤4 tiles for 6+ turns,
        // force BFS toward a firing position regardless of campingTurns.
        // This breaks 4-tile cycles which reset campingTurns on every move.
        bool isLooping = _loopTurns >= 6 && !canFireFromHere;

        bool finisher = target.Health <= 25;

        if ((isCamping || isLooping || (finisher && !canFireFromHere)) && (_campingTurns >= 2 || _loopTurns >= 6))
        {
            var bfsDir = BfsTowardFiringPosition(ctx, me, target);
            if (bfsDir.HasValue)
            {
                Log($"  >>> bfs escape: isCamping={isCamping} isLooping={isLooping} loopTurns={_loopTurns}");
                return new MoveChoice(bfsDir, 1000, isLooping ? "bfs-loop-escape" : "bfs-anti-camp");
            }
        }

        var candidates = new List<MoveChoice>
        {
            ScoreStaying(ctx, me, target, enemies, threats, isCamping)
        };

        foreach (var dir in Directions)
        {
            var (nx, ny) = ApplyDirection(me.X, me.Y, dir);
            if (!IsPassable(ctx, nx, ny))
            {
                continue;
            }

            var c = ScoreMove(ctx, me, nx, ny, dir, target, enemies, threats, isCamping, finisher);
            candidates.Add(c);
        }

        return candidates.OrderByDescending(c => c.Score).First();
    }

    private MoveChoice ScoreStaying(ITurnContext ctx, ITank me, ITank target, ITank[] enemies, ThreatMap threats, bool isCamping)
    {
        int score = ScorePosition(ctx, me.X, me.Y, target, enemies, threats);

        var tile = GetTile(ctx, me.X, me.Y);
        bool canFire = CanFireAtAny(ctx, me.X, me.Y, enemies) && tile.TileType != TileType.Tree;

        if (canFire && !threats.DangerTiles.Contains((me.X, me.Y)))
        {
            score += 25;
        }
        else
        {
            score -= 5;
        }

        if (canFire && _myPendingBullets.Count > 0
            && !threats.DangerTiles.Contains((me.X, me.Y)))
        {
            score += 30;
        }

        if (threats.DangerTiles.Contains((me.X, me.Y)))
        {
            score -= 60;
        }

        // Escalating penalty: grows by 10 per turn on the same tile, capped at 150.
        // This ensures the bot eventually leaves even a good defensive Building
        // position when it's not achieving anything there.
        if (_campingTurns >= 2)
        {
            score -= Math.Min(150, (_campingTurns - 1) * 10);
        }

        if (isCamping)
        {
            score -= 200;
        }

        return new MoveChoice(null, score, "stay");
    }

    private MoveChoice ScoreMove(ITurnContext ctx, ITank me, int nx, int ny, Direction dir,
        ITank target, ITank[] enemies, ThreatMap threats, bool isCamping, bool finisher)
    {
        int score = ScorePosition(ctx, nx, ny, target, enemies, threats);

        // Penalise revisiting any of the last 8 positions.
        // The penalty scales with recency (most recent = highest index in queue order):
        //   visited 1 turn ago  → -80
        //   visited 2 turns ago → -60
        //   visited 3-4 ago     → -40
        //   visited 5-8 ago     → -20
        // The 8-slot window ensures 4-tile cycles don't saturate the entire history,
        // so older entries fall off and the bot can eventually break free.
        int historyPenalty = 0;
        int recentIdx = 0;
        foreach (var prev in _recentPositions)
        {
            if (prev.X == nx && prev.Y == ny)
            {
                int penalty = recentIdx switch { 7 => 80, 6 => 60, 5 => 40, 4 => 40, _ => 20 };
                historyPenalty = Math.Max(historyPenalty, penalty);
            }
            recentIdx++;
        }
        score -= historyPenalty;

        if (isCamping)
        {
            int currentDist = Chebyshev(me.X, me.Y, target.X, target.Y);
            int newDist = Chebyshev(nx, ny, target.X, target.Y);
            if (newDist < currentDist)
            {
                score += 50;
            }
        }

        if (finisher)
        {
            int currentDist = Chebyshev(me.X, me.Y, target.X, target.Y);
            int newDist = Chebyshev(nx, ny, target.X, target.Y);
            if (newDist < currentDist)
            {
                score += 30;
            }
        }

        return new MoveChoice(dir, score, $"move-{dir}");
    }

    private int ScorePosition(ITurnContext ctx, int x, int y, ITank target, ITank[] enemies, ThreatMap threats)
    {
        int score = 0;

        var tile = GetTile(ctx, x, y);
        score += tile.TileType switch
        {
            TileType.Building => 40,
            TileType.Tree     => -25,
            TileType.Sand     => 0,
            TileType.Grass    => 0,
            _                 => -100
        };

        if (threats.DangerTiles.Contains((x, y)))
        {
            score -= 100;
        }

        // Primary target: reward being able to shoot it from this tile.
        bool canFireAtTarget = CanFireAt(ctx, x, y, target) && tile.TileType != TileType.Tree;
        if (canFireAtTarget)
        {
            score += 100;
            if (tile.TileType == TileType.Building)
            {
                score += 30;
            }
        }

        // Distance to primary target.
        int distance = Chebyshev(x, y, target.X, target.Y);
        if (distance >= 4 && distance <= 10)
        {
            score += 20;
        }
        else if (distance < 4)
        {
            score -= 15;
        }
        else if (distance > 14)
        {
            score -= (distance - 14) * 4;
        }

        // Penalise being sandwiched by non-primary enemies.
        // Being close to an enemy we're not focused on is dangerous — they can rotate
        // and shoot us before we react.
        foreach (var e in enemies)
        {
            if (e.OwnerId == target.OwnerId)
            {
                continue;
            }

            int distToOther = Chebyshev(x, y, e.X, e.Y);
            if (distToOther <= 3)
            {
                score -= 40; // very close — high danger
            }
            else if (distToOther <= 6)
            {
                score -= 15; // medium range — moderate risk
            }
        }

        score += CenterBonus(x, y);

        return score;
    }

    private int CenterBonus(int x, int y)
    {
        int cx = _mapWidth / 2;
        int cy = _mapHeight / 2;
        int distFromCenter = Math.Abs(x - cx) + Math.Abs(y - cy);
        int maxDist = (_mapWidth + _mapHeight) / 2;
        return Math.Max(0, 10 - (distFromCenter * 10 / Math.Max(1, maxDist)));
    }

    // ─── Firing decision ──────────────────────────────────────────────────────

    private record FireDecision(TurretDirection Direction, ITank ActualTarget);

    private FireDecision? DecideFiring(ITurnContext ctx, int x, int y, ITank target, ITank[] enemies)
    {
        var tile = GetTile(ctx, x, y);
        if (tile.TileType == TileType.Tree)
        {
            Log($"    cannot fire from Tree at ({x},{y})");
            return null;
        }

        // Try primary target first.
        var bestAim = GetLeadAim(ctx, x, y, target);
        ITank actualTarget = target;

        if (!bestAim.HasValue)
        {
            // Fall back to any other enemy we can shoot this turn.
            foreach (var enemy in enemies)
            {
                if (enemy.OwnerId == target.OwnerId)
                {
                    continue;
                }

                var alt = GetLeadAim(ctx, x, y, enemy);
                if (alt.HasValue)
                {
                    Log($"    no shot on primary, alt target=({enemy.X},{enemy.Y})");
                    bestAim = alt;
                    actualTarget = enemy;
                    break;
                }
            }
        }

        if (!bestAim.HasValue)
        {
            return null;
        }

        if (HasPendingBulletForTarget(actualTarget, bestAim.Value))
        {
            Log($"    skipping shot — already have bullet en route");
            return null;
        }

        return new FireDecision(bestAim.Value, actualTarget);
    }

    private bool HasPendingBulletForTarget(ITank target, TurretDirection myAim)
    {
        foreach (var p in _myPendingBullets)
        {
            // Expired bullets are pruned each turn; skip any that slipped through.
            if (_turnNumber > p.ExpiresOnTurn)
            {
                continue;
            }

            // Only suppress if the in-flight bullet is aimed the same direction.
            // A bullet in a different direction has no chance of hitting the same
            // intercept point, so firing again is worthwhile.
            if (p.TurretId == target.OwnerId && p.Direction == myAim)
            {
                return true;
            }
        }

        return false;
    }

    private void RegisterMyShot(int fromX, int fromY, TurretDirection dir, ITank target)
    {
        int distance = Chebyshev(fromX, fromY, target.X, target.Y);
        // Add 2 extra turns of buffer: bullets travel at roughly 6 tiles/turn, but the
        // target may be further than the Chebyshev distance suggests for diagonal shots,
        // and we want to avoid firing a second identical shot that wastes the cooldown.
        int travelTurns = Math.Max(1, (distance + 5) / 6) + 2;
        int expiry = _turnNumber + travelTurns;
        _myPendingBullets.Add(new PendingBullet(fromX, fromY, dir, _turnNumber, target.OwnerId, expiry));
    }

    private void PruneStalePendingBullets(ITank[] enemies)
    {
        _myPendingBullets.RemoveAll(p => _turnNumber > p.ExpiresOnTurn);
    }

    // ─── Lead aim ─────────────────────────────────────────────────────────────

    private TurretDirection? GetLeadAim(ITurnContext ctx, int fromX, int fromY, ITank target)
    {
        (int vx, int vy) velocity = (0, 0);
        bool isStationary = false;

        if (_enemyHistory.TryGetValue(target.OwnerId, out var hist))
        {
            velocity = (target.X - hist.X, target.Y - hist.Y);
            if (hist.StationaryTurns >= 2 || (velocity.vx == 0 && velocity.vy == 0))
            {
                isStationary = true;
            }
        }

        int currentDistance = Chebyshev(fromX, fromY, target.X, target.Y);
        int travelTurns = Math.Max(1, (currentDistance + 5) / 6);

        if (isStationary)
        {
            var directAim = TurretDirectionTo(fromX, fromY, target.X, target.Y);
            if (directAim.HasValue && HasLineOfSight(ctx, fromX, fromY, target.X, target.Y, directAim.Value))
            {
                return directAim;
            }

            return null;
        }

        // For a moving target, try intercept positions: project where the target
        // will be in 1..travelTurns steps and see if any of those land on a valid
        // 8-direction from us with clear line of sight.
        // We iterate from highest lead (furthest prediction) down to 0 (current pos),
        // so we prefer the furthest-ahead intercept that actually aligns.
        for (int leadSteps = travelTurns; leadSteps >= 0; leadSteps--)
        {
            int px = target.X + velocity.vx * leadSteps;
            int py = target.Y + velocity.vy * leadSteps;

            if ((uint)px >= (uint)_mapWidth || (uint)py >= (uint)_mapHeight)
            {
                continue;
            }

            var aim = TurretDirectionTo(fromX, fromY, px, py);
            if (!aim.HasValue)
            {
                continue;
            }

            if (!HasLineOfSight(ctx, fromX, fromY, px, py, aim.Value))
            {
                continue;
            }

            // Accept the shot: either a direct shot (leadSteps == 0) or the bullet
            // travel time reasonably matches the lead window.  We allow ±1 turn of
            // slack to account for Chebyshev vs. actual travel rounding.
            int leadDistance = Chebyshev(fromX, fromY, px, py);
            int leadTravelTurns = Math.Max(1, (leadDistance + 5) / 6);
            if (leadSteps == 0 || Math.Abs(leadTravelTurns - leadSteps) <= 1)
            {
                return aim;
            }
        }

        return null;
    }

    private bool CanFireAt(ITurnContext ctx, int fromX, int fromY, ITank target)
        => GetLeadAim(ctx, fromX, fromY, target).HasValue;

    /// <summary>Returns true if we have a shot on at least one enemy from this position.</summary>
    private bool CanFireAtAny(ITurnContext ctx, int fromX, int fromY, ITank[] enemies)
    {
        foreach (var e in enemies)
        {
            if (GetLeadAim(ctx, fromX, fromY, e).HasValue)
            {
                return true;
            }
        }
        return false;
    }

    private bool HasLineOfSight(ITurnContext ctx, int fromX, int fromY, int toX, int toY, TurretDirection dir)
    {
        var (dx, dy) = TurretDelta(dir);

        // Fast-reject: if the target tile cannot be reached by stepping (dx,dy)
        // from (fromX, fromY), there is no line of sight regardless of obstacles.
        // This catches the case where TurretDirectionTo snapped to a nearby direction
        // but the target doesn't actually lie on that exact ray.
        if (dx != 0 && dy != 0)
        {
            // Diagonal ray: target must be reachable with equal x and y steps
            int stepX = toX - fromX;
            int stepY = toY - fromY;
            if (Math.Abs(stepX) != Math.Abs(stepY)) return false;
            if (Math.Sign(stepX) != Math.Sign(dx)) return false;
            if (Math.Sign(stepY) != Math.Sign(dy)) return false;
        }
        else if (dx == 0)
        {
            // Vertical ray: target must have same X
            if (toX != fromX) return false;
            if (toY == fromY) return false;
            if (Math.Sign(toY - fromY) != Math.Sign(dy)) return false;
        }
        else
        {
            // Horizontal ray: target must have same Y
            if (toY != fromY) return false;
            if (toX == fromX) return false;
            if (Math.Sign(toX - fromX) != Math.Sign(dx)) return false;
        }

        int cx = fromX, cy = fromY;
        int maxSteps = Math.Max(_mapWidth, _mapHeight);

        for (int step = 1; step <= maxSteps; step++)
        {
            cx += dx;
            cy += dy;

            if ((uint)cx >= (uint)_mapWidth || (uint)cy >= (uint)_mapHeight)
            {
                return false;
            }

            if (cx == toX && cy == toY)
            {
                return true;
            }

            var type = GetTile(ctx, cx, cy).TileType;
            if (type == TileType.Tree || type == TileType.Building)
            {
                return false;
            }
        }

        return false;
    }

    // ─── Target selection ────────────────────────────────────────────────────

    private ITank PickTarget(ITurnContext ctx, ITank me, ITank[] enemies)
    {
        ITank best = enemies[0];
        int bestScore = int.MinValue;

        foreach (var e in enemies)
        {
            int score = 0;
            score -= e.Health;
            int dist = Chebyshev(me.X, me.Y, e.X, e.Y);
            score -= dist * 2;

            if (CanFireAt(ctx, me.X, me.Y, e))
            {
                score += 30;
            }

            var aimAtMe = TurretDirectionTo(e.X, e.Y, me.X, me.Y);
            if (aimAtMe.HasValue && aimAtMe.Value == e.TurretDirection)
            {
                score += 20;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = e;
            }
        }

        return best;
    }

    // ─── Reposition ───────────────────────────────────────────────────────────

    private Direction? BfsTowardFiringPosition(ITurnContext ctx, ITank me, ITank target)
    {
        // Search deeper on larger maps — depth 12 is too shallow when the enemy
        // is 15+ tiles away. Use map size to set a sensible ceiling.
        int maxSearchDepth = Math.Max(16, (_mapWidth + _mapHeight) / 2);

        var visited = new bool[_mapWidth * _mapHeight];
        var firstStep = new Direction?[_mapWidth * _mapHeight];
        var depth = new int[_mapWidth * _mapHeight];

        Queue<(int x, int y)> queue = [];
        int startIdx = me.Y * _mapWidth + me.X;
        visited[startIdx] = true;
        queue.Enqueue((me.X, me.Y));

        Direction? bestDir = null;
        int bestScore = int.MinValue;

        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            int idx = y * _mapWidth + x;

            if (depth[idx] >= maxSearchDepth)
            {
                continue;
            }

            foreach (var dir in Directions)
            {
                var (nx, ny) = ApplyDirection(x, y, dir);
                if ((uint)nx >= (uint)_mapWidth || (uint)ny >= (uint)_mapHeight)
                {
                    continue;
                }

                int nIdx = ny * _mapWidth + nx;
                if (visited[nIdx])
                {
                    continue;
                }

                visited[nIdx] = true;

                if (!IsPassable(ctx, nx, ny))
                {
                    continue;
                }

                depth[nIdx] = depth[idx] + 1;
                firstStep[nIdx] = firstStep[idx] ?? dir;

                queue.Enqueue((nx, ny));

                var tile = GetTile(ctx, nx, ny);
                if (tile.TileType == TileType.Tree)
                {
                    continue;
                }

                if (!CanFireAt(ctx, nx, ny, target))
                {
                    continue;
                }

                int score = 200 - depth[nIdx] * 10;
                if (tile.TileType == TileType.Building)
                {
                    score += 30;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestDir = firstStep[nIdx];
                }
            }
        }

        // If BFS found no firing tile within search depth, fall back to moving
        // directly toward the enemy. This keeps the bot closing distance rather
        // than staying stuck when the map is large or the enemy is far away.
        if (bestDir == null)
        {
            bestDir = DirectionToward(me.X, me.Y, target.X, target.Y);
            if (bestDir.HasValue)
            {
                var (nx, ny) = ApplyDirection(me.X, me.Y, bestDir.Value);
                if (!IsPassable(ctx, nx, ny))
                {
                    bestDir = null;
                }
            }
        }

        return bestDir;
    }

    private void TryReposition(ITurnContext ctx, ITank me)
    {
        int cx = _mapWidth / 2;
        int cy = _mapHeight / 2;
        var dir = DirectionToward(me.X, me.Y, cx, cy);
        if (!dir.HasValue)
        {
            return;
        }

        var (nx, ny) = ApplyDirection(me.X, me.Y, dir.Value);
        if (IsPassable(ctx, nx, ny))
        {
            ctx.MoveTank(dir.Value);
        }
    }

    // ─── Bookkeeping ─────────────────────────────────────────────────────────

    private void EndTurn(ITank me, ITank[] enemies)
    {
        foreach (var e in enemies)
        {
            int prevStationary = 0;
            if (_enemyHistory.TryGetValue(e.OwnerId, out var prev))
            {
                if (prev.X == e.X && prev.Y == e.Y)
                {
                    prevStationary = prev.StationaryTurns + 1;
                }
            }

            _enemyHistory[e.OwnerId] = new EnemyHistory(e.X, e.Y, e.Health, e.TurretDirection, prevStationary);
        }

        if (_campingPosition.HasValue
            && _campingPosition.Value.X == me.X
            && _campingPosition.Value.Y == me.Y)
        {
            _campingTurns++;
        }
        else
        {
            _campingTurns = 0;
            _campingPosition = (me.X, me.Y);
        }

        if (_recentPositions.Count >= 8)
        {
            _recentPositions.Dequeue();
        }
        _recentPositions.Enqueue((me.X, me.Y));

        // Loop detection: if the last 8 positions cover only 4 or fewer unique tiles,
        // we're in a cycle. Increment a counter so ChooseBestMove can force an escape.
        if (_recentPositions.Count >= 8)
        {
            int uniqueCount = new HashSet<(int X, int Y)>(_recentPositions).Count;
            if (uniqueCount <= 4)
            {
                _loopTurns++;
            }
            else
            {
                _loopTurns = 0;
                _loopWindow.Clear();
            }
        }
    }

    private static ITank[] GetEnemies(ITank[] tanks, int myId)
    {
        var result = new List<ITank>(tanks.Length);
        foreach (var t in tanks)
        {
            if (t.OwnerId != myId && !t.Destroyed)
            {
                result.Add(t);
            }
        }

        return [.. result];
    }

    // ─── Geometry & helpers ──────────────────────────────────────────────────

    private static int Chebyshev(int x1, int y1, int x2, int y2)
        => Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    private static Direction? DirectionToward(int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX;
        int dy = toY - fromY;
        if (dx == 0 && dy == 0)
        {
            return null;
        }

        return Math.Abs(dx) >= Math.Abs(dy)
            ? (dx > 0 ? Direction.West : Direction.East)
            : (dy > 0 ? Direction.North : Direction.South);
    }

    /// <summary>
    /// Returns the enemy's current facing direction plus the two immediately
    /// adjacent directions in the 8-direction ring. This gives a 3-direction
    /// danger cone that's realistic without flooding the whole map.
    /// </summary>
    private static TurretDirection[] AdjacentTurretDirections(TurretDirection facing)
    {
        // Ordered ring of all 8 directions
        ReadOnlySpan<TurretDirection> ring =
        [
            TurretDirection.North, TurretDirection.NorthEast,
            TurretDirection.East,  TurretDirection.SouthEast,
            TurretDirection.South, TurretDirection.SouthWest,
            TurretDirection.West,  TurretDirection.NorthWest
        ];

        int idx = 0;
        for (int i = 0; i < ring.Length; i++)
        {
            if (ring[i] == facing) { idx = i; break; }
        }

        return
        [
            ring[(idx + 7) % 8], // one step CCW
            facing,
            ring[(idx + 1) % 8]  // one step CW
        ];
    }

    private static (int dx, int dy) TurretDelta(TurretDirection dir) => dir switch
    {
        TurretDirection.North     => ( 0,  1),
        TurretDirection.South     => ( 0, -1),
        TurretDirection.East      => (-1,  0),
        TurretDirection.West      => ( 1,  0),
        TurretDirection.NorthEast => (-1,  1),
        TurretDirection.NorthWest => ( 1,  1),
        TurretDirection.SouthEast => (-1, -1),
        TurretDirection.SouthWest => ( 1, -1),
        _                         => ( 0,  0)
    };

    private static (int x, int y) ApplyDirection(int x, int y, Direction dir) => dir switch
    {
        Direction.North => (x,     y + 1),
        Direction.South => (x,     y - 1),
        Direction.East  => (x - 1, y),
        Direction.West  => (x + 1, y),
        _               => (x,     y)
    };

    private static TurretDirection? TurretDirectionTo(int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX;
        int dy = toY - fromY;
        if (dx == 0 && dy == 0)
        {
            return null;
        }

        // Snap to the nearest of the 8 valid turret directions.
        // Divide the plane into 8 sectors of 45° each. Each diagonal sector spans
        // the range where one axis is within ~2.4× the other (tan(67.5°) ≈ 2.414).
        int absDx = Math.Abs(dx);
        int absDy = Math.Abs(dy);

        // Cardinal directions: one axis dominates by more than 2.414:1
        if (absDy == 0 || absDx > absDy * 241 / 100)
        {
            // Primarily horizontal
            return dx < 0 ? TurretDirection.East : TurretDirection.West;
        }
        if (absDx == 0 || absDy > absDx * 241 / 100)
        {
            // Primarily vertical
            return dy > 0 ? TurretDirection.North : TurretDirection.South;
        }

        // Diagonal — both axes within 2.414:1 of each other
        return (Math.Sign(dx), Math.Sign(dy)) switch
        {
            (-1,  1) => TurretDirection.NorthEast,
            ( 1,  1) => TurretDirection.NorthWest,
            (-1, -1) => TurretDirection.SouthEast,
            ( 1, -1) => TurretDirection.SouthWest,
            _        => null
        };
    }

    private ITile GetTile(ITurnContext ctx, int x, int y) => ctx.GetTile(x, y);

    private bool IsPassable(ITurnContext ctx, int x, int y)
    {
        if ((uint)x >= (uint)_mapWidth || (uint)y >= (uint)_mapHeight)
        {
            return false;
        }

        var type = GetTile(ctx, x, y).TileType;
        if (type == TileType.Water)
        {
            return false;
        }

        var myId = ctx.Tank.OwnerId;
        foreach (var t in ctx.GetTanks())
        {
            if (t.Destroyed)
            {
                continue;
            }

            if (t.OwnerId == myId)
            {
                continue;
            }

            if (t.X == x && t.Y == y)
            {
                return false;
            }
        }

        return true;
    }
}