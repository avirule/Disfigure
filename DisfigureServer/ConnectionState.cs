namespace DisfigureServer
{
    public enum ConnectionState
    {
        Idle,
        ReadingHeader,
        ReadingContent
    }
}
