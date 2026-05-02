using ComCross.Shell.Services;

namespace ComCross.Shell.ViewModels;

public interface IItemVmFactory<TVm, in TContext>
    where TVm : notnull, IInitializable<TContext>
{
    TVm Create(TContext context);
}

public sealed class ItemVmFactory<TVm, TContext> : IItemVmFactory<TVm, TContext>
    where TVm : notnull, IInitializable<TContext>
{
    private readonly IObjectFactory _objectFactory;

    public ItemVmFactory(IObjectFactory objectFactory)
    {
        _objectFactory = objectFactory;
    }

    public TVm Create(TContext context)
    {
        var viewModel = _objectFactory.Create<TVm>();
        viewModel.Init(context);
        return viewModel;
    }
}
