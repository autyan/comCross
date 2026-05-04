using ComCross.Shared.Models;

namespace ComCross.Shared.Interfaces;

public interface IStoragePolicyService
{
    StoragePolicy Current { get; }

    event Action<StoragePolicy>? PolicyChanged;
}
