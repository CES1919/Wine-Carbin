namespace WineCabinet;

internal enum DragEdgeAction
{
    None,
    PreviousPage,
    NextPage,
    Remove
}

internal static class DragNavigation
{
    public static DragEdgeAction Decide(Rectangle bounds, Point cursor, int page, int maxPage)
    {
        if (cursor.X < bounds.Left || cursor.X > bounds.Right) return DragEdgeAction.Remove;
        if (cursor.Y < bounds.Top) return page > 0 ? DragEdgeAction.PreviousPage : DragEdgeAction.Remove;
        if (cursor.Y > bounds.Bottom) return page < maxPage ? DragEdgeAction.NextPage : DragEdgeAction.Remove;
        return DragEdgeAction.None;
    }
}
