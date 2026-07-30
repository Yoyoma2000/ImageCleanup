using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace ImageCleanup.App.ViewModels;

/// <summary>
/// Shared helper for view models that expose a lazily-loaded thumbnail
/// <see cref="ImageSource"/>. Thumbnail byte generation runs off the UI
/// thread; only the final BitmapImage decode is marshalled back via the
/// captured <see cref="DispatcherQueue"/>.
/// </summary>
internal static class ThumbnailLoader
{
    public static void RequestThumbnail(
        DispatcherQueue dispatcher,
        Func<byte[]?> generateBytes,
        Action<ImageSource> onLoaded)
    {
        _ = LoadAsync(dispatcher, generateBytes, onLoaded);
    }

    private static async Task LoadAsync(
        DispatcherQueue dispatcher,
        Func<byte[]?> generateBytes,
        Action<ImageSource> onLoaded)
    {
        var bytes = await Task.Run(generateBytes);
        if (bytes is null) return;

        dispatcher.TryEnqueue(async () =>
        {
            using var stream = new InMemoryRandomAccessStream();
            await stream.WriteAsync(bytes.AsBuffer());
            stream.Seek(0);

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);
            onLoaded(bitmap);
        });
    }
}
