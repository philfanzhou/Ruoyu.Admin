namespace Ruoyu.Admin.ServiceClients;

public abstract class MistakeBoundaryException : Exception
{
    protected MistakeBoundaryException() : base("Mistake service request rejected.") { }
}
