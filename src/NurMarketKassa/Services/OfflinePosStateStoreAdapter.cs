namespace NurMarketKassa.Services;

public interface IOfflinePosStateStore
{
    void SaveFromApp(decimal? shiftBalance);
}

public sealed class OfflinePosStateStoreAdapter : IOfflinePosStateStore
{
    public void SaveFromApp(decimal? shiftBalance) => OfflinePosStateStore.SaveFromApp(shiftBalance ?? 0m);
}
