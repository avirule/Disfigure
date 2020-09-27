#region

using System.Runtime.CompilerServices;
using System.Threading.Tasks;

#endregion

namespace Disfigure
{
    public static class TaskExtensions
    {
        public static ConfiguredTaskAwaitable Contextless(this Task task) => task.ConfigureAwait(false);
        public static ConfiguredTaskAwaitable<T> Contextless<T>(this Task<T> task) => task.ConfigureAwait(false);
    }

    public static class ValueTaskExtensions
    {
        public static ConfiguredValueTaskAwaitable Contextless(this ValueTask valueTask) => valueTask.ConfigureAwait(false);
        public static ConfiguredValueTaskAwaitable<T> Contextless<T>(this ValueTask<T> valueTask) => valueTask.ConfigureAwait(false);
    }
}
