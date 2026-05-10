using System;
using System.Collections.Generic;
using System.Linq;
using Umbra.Router.Core.Work.Navigation;

namespace OrionBe.Extensions;

public static class NavigationContextExtension
{
    public static bool IsActiveExact(this NavigationContext context, string route)
    {
        var current = new RouteSnapshot(context.CurrentUrl).Segments;
        var target = new  RouteSnapshot(route).Segments;
        
        var segments = NormalizeSegments(current, target);
        
        return segments.CurrentSegments.SequenceEqual(segments.TargetSegments);
    }

    public static bool IsActiveExact(this NavigationContext context, string[] segments)
        => IsActiveExact(context, string.Join('/', segments));
    
    public static bool IsActive(this NavigationContext context, string route)
    {
        var current = new RouteSnapshot(context.CurrentUrl).Segments;
        var target = new RouteSnapshot(route).Segments;
        
        var segments = NormalizeSegments(current, target);

        if (segments.TargetSegments.Length > segments.CurrentSegments.Length)
            return false;

        return segments.CurrentSegments
            .Take(segments.TargetSegments.Length)
            .SequenceEqual(segments.TargetSegments);
    }

    public static bool IsActive(this NavigationContext context, string[] segments)
        => IsActive(context, string.Join('/', segments));

    private static RouterMatch NormalizeSegments(string[] current, string[] target)
    {
        List<int> removeIndex = new List<int>();


        for (int i = 0; i < target.Length; i++)
            if(target[i].StartsWith(':'))
                removeIndex.Add(i);

        return new RouterMatch()
        {
            CurrentSegments = current
                .Where((s, i) => !removeIndex.Contains(i))
                .ToArray(),
            TargetSegments = target.Where((s, i) => !removeIndex.Contains(i))
                .ToArray()
        };

    }
    
    private sealed class RouterMatch
    {
        public string[] CurrentSegments { get; set; } = [];

        public string[] TargetSegments { get; set; } = [];
    }
}