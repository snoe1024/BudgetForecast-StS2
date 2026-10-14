using System.Runtime.CompilerServices;
using BudgetForecast.BudgetForecastCode.Forecast;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace BudgetForecast.BudgetForecastCode.Presentation;

internal static class RoutePreview
{
    // A screen owns its cache and one reusable native Line2D; no new script nodes or input handlers.
    private sealed class ScreenState
    {
        public readonly RouteCache Cache = new();
        public Line2D? Line;
        public NNormalMapPoint? Owner;
        public IRunState? Run;
        public MapPoint? Source;
        public IReadOnlyList<MapPoint>? Path;
    }

    private static readonly ConditionalWeakTable<NMapScreen, ScreenState> Screens = new();

    public static IReadOnlyList<MapPoint>? GetRoute(NMapScreen screen, IRunState run,
        MapPoint current, MapPoint shop, out bool cacheHit) =>
        Screens.GetValue(screen, _ => new ScreenState()).Cache.GetOrCompute(run.Map, current, shop,
            CombatMaximizingRoute.StrategyId, CombatMaximizingRoute.Find, out cacheHit);

    public static void Show(NMapScreen screen, NNormalMapPoint owner, IRunState run,
        MapPoint source, IReadOnlyList<MapPoint> path)
    {
        var state = Screens.GetValue(screen, _ => new ScreenState());
        state.Owner = owner;
        state.Run = run;
        state.Source = source;
        state.Path = path;
        if (!GodotObject.IsInstanceValid(state.Line))
        {
            var parent = screen.GetNode<Control>("TheMap");
            var dots = screen.GetNode<Control>("TheMap/Paths");
            state.Line = new Line2D
            {
                Name = "BudgetForecastRoute",
                Width = 28f, // Native dots are 16px (19.2px after their traveled scale).
                DefaultColor = new Color(0.15f, 0.65f, 0.95f, 0.7f),
                JointMode = Line2D.LineJointMode.Round,
                BeginCapMode = Line2D.LineCapMode.Round,
                EndCapMode = Line2D.LineCapMode.Round,
                Antialiased = true,
                ZIndex = dots.ZIndex
            };
            parent.AddChild(state.Line);
            parent.MoveChild(state.Line, dots.GetIndex()); // MapBg < route < Paths < Points.
        }
        UpdatePoints(screen, state);
        state.Line.Visible = true;
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
