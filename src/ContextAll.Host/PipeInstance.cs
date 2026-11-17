namespace ContextAll.Host;

public static class PipeInstance
{
    public static Mutex? TryClaim(string userSid)
    {
        var name = "Local\\ContextAll-serve-" + userSid.Replace('\\', '-').Replace('/', '-');
        var mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        if (createdNew)
        {
            return mutex;
        }

        mutex.Dispose();
        return null;
    }
}
