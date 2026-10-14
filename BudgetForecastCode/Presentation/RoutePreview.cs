using System.Runtime.CompilerServices;
using BudgetForecast.BudgetForecastCode.Forecast;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace BudgetForecast.BudgetForecastCode.Presentation;

internal static class RoutePreview
{
    // A screen owns its cache and one reusable native Line2D; no custom script nodes.
    private sealed class ScreenState
    {
        public readonly RouteCache Cache = new();
        public Line2D? Line;
        public NNormalMapPoint? Owner;
        public IRunState? Run;
        public MapPoint? Source;
        public IReadOnlyList<MapPoint>? Path;
        public RouteStrategy Strategy = RouteStrategy.Balance;
        public bool ShowAverage;
    }

    private static readonly ConditionalWeakTable<NMapScreen, ScreenState> Screens = new();

    public static IReadOnlyList<MapPoint>? GetRoute(NMapScreen screen, IRunState run,
        MapPoint current, MapPoint shop, out bool cacheHit)
    {
        var state = Screens.GetValue(screen, _ => new ScreenState());
        var path = state.Cache.GetOrCompute(run.Map, current, shop, state.Strategy.ToString(),
            (from, to) => RouteStrategies.Find(from, to, state.Strategy), out cacheHit);
        if (path != null)
            MainFile.Logger.Info($"Route cache {(cacheHit ? "hit" : "miss")}: shop=({shop.coord.col},{shop.coord.row}); strategy={state.Strategy}");
        return path;
    }

    public static void Show(NMapScreen screen, NNormalMapPoint owner, IRunState run,
        MapPoint source, IReadOnlyList<MapPoint> path)
    {
        var state = Screens.GetValue(screen, _ => new ScreenState());
        state.Owner = owner;
        state.Run = run;
        state.Source = source;
        state.Path = path;
        state.ShowAverage = Input.IsKeyPressed(Key.Shift);
        if (!GodotObject.IsInstanceValid(state.Line))
        {
            var parent = screen.GetNode<Control>("TheMap");
            var dots = screen.GetNode<Control>("TheMap/Paths");
            state.Line = new Line2D
            {
                Name = "BudgetForecastRoute",
                Width = 28f, // Native dots are 16px (19.2px after their traveled scale).
                DefaultColor = RouteStrategies.Color(state.Strategy),
                JointMode = Line2D.LineJointMode.Round,
                BeginCapMode = Line2D.LineCapMode.Round,
                EndCapMode = Line2D.LineCapMode.Round,
                Antialiased = true,
                ZIndex = dots.ZIndex
            };
            parent.AddChild(state.Line);
            parent.MoveChild(state.Line, dots.GetIndex()); // MapBg < route < Paths < Points.
        }
        var player = LocalContext.GetMe(run);
        if (player != null)
        {
            var estimate = CombatGoldCalculator.Sum(path, run.AscensionLevel);
            ForecastLog.Write(source, path, player.Gold, run.AscensionLevel, estimate, state.Strategy.ToString());
            ForecastHoverTip.Show(owner, run, estimate, state.Strategy, state.ShowAverage);
        }
        state.Line.DefaultColor = RouteStrategies.Color(state.Strategy);
        UpdatePoints(screen, state);
        state.Line.Visible = true;
    }

    public static bool HandleWheel(NMapScreen screen, InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton wheel || !wheel.ShiftPressed ||
            wheel.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown) ||
            !Screens.TryGetValue(screen, out var state) || state.Owner == null || state.Run == null ||
            !GodotObject.IsInstanceValid(state.Owner) || !screen.IsOpen || screen.IsTraveling ||
            !ActiveScreenContext.Instance.IsCurrent(screen) || screen.Drawings.GetLocalDrawingMode() != DrawingMode.None ||
            !Traverse.Create(state.Owner).Field("_isHovered").GetValue<bool>() ||
            !new Rect2(Vector2.Zero, state.Owner.Size).HasPoint(state.Owner.GetLocalMousePosition())) return false;
        screen.GetViewport().SetInputAsHandled(); // Consume both press/release; switch once on press.
        if (!wheel.Pressed) return true;
        try
        {
            var current = state.Run.CurrentMapPoint;
            if (current == null || !state.Cache.IsFor(state.Run.Map, current))
            {
                Invalidate(screen);
                return true;
            }
            state.Strategy = RouteStrategies.Next(state.Strategy, wheel.ButtonIndex == MouseButton.WheelDown ? 1 : -1);
            var path = GetRoute(screen, state.Run, current, state.Owner.Point, out _);
            if (path != null) Show(screen, state.Owner, state.Run, current, path);
        }
        catch (Exception exception)
        {
            MainFile.Logger.Warn($"Strategy switch failed: {exception}");
        }
        return true;
    }

    public static void Refresh(NMapScreen screen)
    {
        if (!Screens.TryGetValue(screen, out var state) || state.Owner == null) return;
        try
        {
            var current = state.Run?.CurrentMapPoint;
            if (current == null || !state.Cache.IsFor(state.Run!.Map, current))
            {
                Invalidate(screen);
                return;
            }
            if (!screen.IsOpen || screen.IsTraveling || !GodotObject.IsInstanceValid(state.Owner) ||
                !Traverse.Create(state.Owner).Property("IsFocused").GetValue<bool>() || !state.Owner.IsVisibleInTree() ||
                screen.Drawings.GetLocalDrawingMode() != DrawingMode.None)
            {
                Hide(screen);
                return;
            }
            UpdatePoints(screen, state); // Follow resize/point animations without recalculating the route.
            var showAverage = Input.IsKeyPressed(Key.Shift);
            if (state.ShowAverage != showAverage)
            {
                state.ShowAverage = showAverage;
                var player = LocalContext.GetMe(state.Run);
                if (player != null)
                    ForecastHoverTip.Show(state.Owner, state.Run,
                        CombatGoldCalculator.Sum(state.Path!, state.Run.AscensionLevel), state.Strategy, showAverage);
            }
        }
        catch (Exception exception)
        {
            Hide(screen);
            MainFile.Logger.Warn($"Route preview failed: {exception}");
        }
    }

    private static void UpdatePoints(NMapScreen screen, ScreenState state)
    {
        var nodes = Traverse.Create(screen).Field("_mapPointDictionary")
            .GetValue<Dictionary<MapCoord, NMapPoint>>();
        var route = new[] { state.Source! }.Concat(state.Path!).ToArray();
        var positions = new Vector2[route.Length];
        for (var i = 0; i < route.Length; i++)
        {
            var node = nodes[route[i].coord];
            // Icons have their own size, pivot, hover scale and rotation. Connect their visual centers.
            var icon = node.GetNodeOrNull<Control>("%Icon") ?? node;
            positions[i] = state.Line!.ToLocal(icon.GetGlobalTransform() * (icon.Size * 0.5f));
        }
        state.Line!.Points = positions;
    }

    public static void HideFor(NNormalMapPoint owner)
    {
        var screen = Traverse.Create(owner).Field("_screen").GetValue<NMapScreen>();
        if (screen != null && Screens.TryGetValue(screen, out var state) && ReferenceEquals(state.Owner, owner))
            Hide(screen);
    }

    public static void Hide(NMapScreen screen)
    {
        if (!Screens.TryGetValue(screen, out var state)) return;
        if (GodotObject.IsInstanceValid(state.Line)) state.Line!.Visible = false;
        state.Owner = null;
        state.Run = null;
        state.Source = null;
        state.Path = null;
    }

    public static void Invalidate(NMapScreen screen)
    {
        Hide(screen);
        if (Screens.TryGetValue(screen, out var state)) state.Cache.Clear();
    }

    public static void InvalidateAll()
    {
        foreach (var entry in Screens) Invalidate(entry.Key);
    }
}
