namespace ComCross.Shell.ViewModels;

public interface IInitializable<in TContext>
{
    void Init(TContext context);
}
