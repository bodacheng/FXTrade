namespace TestFXTrade.Fx.Domain
{
    public static class FxConstants
    {
        public const string UsdJpySymbol = "USD/JPY";
        public const double UsdJpyPipSize = 0.01d;
        public const double StandardLotBaseUnits = 100000d;
        public const double UsdJpyPipValueJpyPerLot = StandardLotBaseUnits * UsdJpyPipSize;
        public const double MinTradableLot = 0.01d;
    }
}
