using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CurseforgeModpackLoader;

public static class HttpClientProgressExtensions
{
	public static async Task DownloadDataAsync(this HttpClient client, string requestUrl, Stream destination,
		IProgress<FileLoadingProgress> progress = null, CancellationToken cancellationToken = default)
	{
		using var response = await client.GetAsync(requestUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
		var contentLength = response.Content.Headers.ContentLength;
		await using var download = await response.Content.ReadAsStreamAsync(cancellationToken);
		// no progress... no contentLength... very sad
		if (progress is null || !contentLength.HasValue)
		{
			await download.CopyToAsync(destination, cancellationToken);
			return;
		}

		// Such progress and contentLength much reporting Wow!
		long prevBytes = 0;
		float speed = 0;
		var prevTime = DateTime.Now;
		var progressWrapper = new Progress<long>(totalBytes =>
		{
			var delta = DateTime.Now.Subtract(prevTime);
			if (delta.TotalMilliseconds >= 100)
			{
				speed = 8f * (totalBytes - prevBytes) / (float) delta.TotalSeconds;
				prevBytes = totalBytes;
				prevTime = DateTime.Now;
			}
			progress.Report(new FileLoadingProgress(totalBytes, contentLength.Value, speed));
		});
		await download.CopyToAsync(destination, 81920, progressWrapper, cancellationToken);
	}

	static async Task CopyToAsync(this Stream source, Stream destination, int bufferSize,
		IProgress<long> progress = null, CancellationToken cancellationToken = default)
	{
		if (bufferSize < 0)
			throw new ArgumentOutOfRangeException(nameof(bufferSize));
		if (source is null)
			throw new ArgumentNullException(nameof(source));
		if (!source.CanRead)
			throw new InvalidOperationException($"'{nameof(source)}' is not readable.");
		if (destination == null)
			throw new ArgumentNullException(nameof(destination));
		if (!destination.CanWrite)
			throw new InvalidOperationException($"'{nameof(destination)}' is not writable.");

		var buffer = new byte[bufferSize];
		long totalBytesRead = 0;
		int bytesRead;
		while ((bytesRead =
			       await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
		{
			await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
			totalBytesRead += bytesRead;
			progress?.Report(totalBytesRead);
		}
	}
	
	public readonly struct FileLoadingProgress(long loadedBytes, long totalBytes, float speed)
	{
		public long LoadedBytes => loadedBytes;
		public long TotalBytes => totalBytes;
		public float Speed => speed;
		public float Percentage => (float) loadedBytes / totalBytes;
	}
}