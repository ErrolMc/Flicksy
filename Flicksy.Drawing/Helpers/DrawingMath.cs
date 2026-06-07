using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Flicksy.Drawing.Source;
using System.Windows.Media;

namespace Flicksy.Drawing.Helpers;

/// <summary>
/// Stateless geometry helpers shared by the drawing/selection code.
/// </summary>
internal static class DrawingMath
{
    public static double DistanceSquared(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }

    public static double DistanceSquaredToSegment(Point point, Point start, Point end)
    {
        double vx = end.X - start.X;
        double vy = end.Y - start.Y;
        double lengthSquared = (vx * vx) + (vy * vy);
        if (lengthSquared <= double.Epsilon)
        {
            return DistanceSquared(point, start);
        }

        double t = ((point.X - start.X) * vx + (point.Y - start.Y) * vy) / lengthSquared;
        t = Math.Clamp(t, 0d, 1d);

        var closest = new Point(start.X + (t * vx), start.Y + (t * vy));
        return DistanceSquared(point, closest);
    }

    /// <summary>
    /// Picks the most appropriate resize cursor for the given diagonal direction vector
    /// (bidirectional). Buckets across the 4 cardinal/diagonal axes.
    /// </summary>
    public static Cursor CursorForDiagonal(Vector diagonal)
    {
        if (Math.Abs(diagonal.X) < double.Epsilon && Math.Abs(diagonal.Y) < double.Epsilon)
        {
            return Cursors.SizeNWSE;
        }

        double angle = Math.Atan2(diagonal.Y, diagonal.X);
        if (angle < 0) angle += Math.PI; // direction is bidirectional
        double deg = angle * 180.0 / Math.PI;

        // 4 buckets across [0, 180):
        //   [  0,  22.5) and [157.5, 180) → horizontal (WE)
        //   [ 22.5,  67.5) → top-left to bottom-right (NWSE)
        //   [ 67.5, 112.5) → vertical (NS)
        //   [112.5, 157.5) → top-right to bottom-left (NESW)
        if (deg < 22.5 || deg >= 157.5) 
            return Cursors.SizeWE;
        if (deg < 67.5) 
            return Cursors.SizeNWSE;
        if (deg < 112.5) 
            return Cursors.SizeNS;
        return Cursors.SizeNESW;
    }

    /// <summary>
    /// Returns true if the world-space point lands on the given item, taking the item's
    /// transform into account.
    /// </summary>
    public static bool IntersectsItem(DrawingItem item, Point worldPoint)
    {
        Matrix inverse = item.Transform.Matrix;
        if (!inverse.HasInverse)
        {
            return false;
        }
        inverse.Invert();
        Point localPoint = inverse.Transform(worldPoint);
        return item.HitTest(localPoint);
    }

    /// <summary>
    /// Returns the topmost item (highest z-order in the list) that intersects the world-space
    /// point, or null if none do.
    /// </summary>
    public static DrawingItem? HitTestTopmost(IList<DrawingItem> items, Point worldPoint)
    {
        for (var i = items.Count - 1; i >= 0; i--)
        {
            DrawingItem item = items[i];
            if (IntersectsItem(item, worldPoint))
            {
                return item;
            }
        }
        return null;
    }
}
