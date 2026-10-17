namespace WineCabinet;

internal static class GridLayoutRules
{
    private static readonly int[] NineGridItemSlots = { 0, 1, 2, 3, 5, 6, 7, 8 };
    private static readonly int[] SixteenGridItemSlots = { 0, 1, 2, 4, 6, 8, 9, 10, 3, 7, 11, 12, 13, 14, 15 };

    public static int ItemsPerPage(int dimension) => dimension switch
    {
        3 => 8,
        4 => 15,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };

    public static int CenterSlot(int dimension) => dimension switch
    {
        3 => 4,
        4 => 5,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };

    public static int[] ItemSlots(int dimension) => dimension switch
    {
        3 => NineGridItemSlots,
        4 => SixteenGridItemSlots,
        _ => throw new ArgumentOutOfRangeException(nameof(dimension))
    };
}
