using System.Buffers;
using System.Net.Http;
using System.Threading;
using SkiaSharp;

namespace IpCamera.Services
{
	public sealed class MjpegStreamer : IAsyncDisposable
	{
		private readonly HttpClient _httpClient;
		private CancellationTokenSource? _cts;
		private Task? _readTask;

		public event Action<byte[]>? FrameReceived;
		public event Action? MotionDetected;
		public event Action<string>? Error;
		public event Action<float,int,int>? Metrics; // ratio, changed, total pixels

		// Detection params
		private SKBitmap? _previousFrame;
		private readonly object _stateLock = new();
		private readonly int _downscaleWidth;
		private readonly int _downscaleHeight;
		private readonly float _differenceThresholdRatio;
		private readonly byte _perChannelThreshold;
		private long _lastDetectionMs;
		private readonly int _cooldownMs;

		public MjpegStreamer(HttpClient httpClient,
			int downscaleWidth = 96,
			int downscaleHeight = 72,
			float differenceThresholdRatio = 0.015f,
			byte perChannelThreshold = 18,
			int cooldownMs = 2000)
		{
			_httpClient = httpClient;
			_downscaleWidth = downscaleWidth;
			_downscaleHeight = downscaleHeight;
			_differenceThresholdRatio = differenceThresholdRatio;
			_perChannelThreshold = perChannelThreshold;
			_cooldownMs = cooldownMs;
		}

		public void Start(string url)
		{
			_cts?.Cancel();
			_cts = new CancellationTokenSource();
			_readTask = Task.Run(() => ReadLoopAsync(url, _cts.Token));
		}

		public async ValueTask DisposeAsync()
		{
			_cts?.Cancel();
			if (_readTask != null)
			{
				try { await _readTask.ConfigureAwait(false); } catch { }
			}
			_cts?.Dispose();
			_cts = null;
			lock (_stateLock)
			{
				_previousFrame?.Dispose();
				_previousFrame = null;
			}
		}

		private async Task ReadLoopAsync(string url, CancellationToken cancellationToken)
		{
			try
			{
				using var request = new HttpRequestMessage(HttpMethod.Get, url);
				request.Headers.TryAddWithoutValidation("User-Agent", "IpCamera/1.0 (MJPEG)");
				using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
				response.EnsureSuccessStatusCode();

				await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
				var buffer = ArrayPool<byte>.Shared.Rent(1024 * 64);
				var readBuffer = new List<byte>(1024 * 256);
				try
				{
					while (!cancellationToken.IsCancellationRequested)
					{
						var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
						if (bytesRead <= 0) break;

						for (int i = 0; i < bytesRead; i++)
						{
							readBuffer.Add(buffer[i]);
						}

						// Extract JPEG frames by SOI/EOI markers
						while (true)
						{
							int soi = IndexOfPattern(readBuffer, 0xFF, 0xD8);
							if (soi < 0) { break; }
							int eoi = IndexOfPattern(readBuffer, 0xFF, 0xD9, soi + 2);
							if (eoi < 0) { break; }

							int length = (eoi + 2) - soi;
							var frameBytes = readBuffer.GetRange(soi, length).ToArray();
							readBuffer.RemoveRange(0, eoi + 2);

							OnFrame(frameBytes);
						}
					}
				}
				finally
				{
					ArrayPool<byte>.Shared.Return(buffer);
				}
			}
			catch (Exception ex)
			{
				Error?.Invoke(ex.Message);
			}
		}

		private static int IndexOfPattern(List<byte> data, byte b1, byte b2, int start = 0)
		{
			for (int i = start; i < data.Count - 1; i++)
			{
				if (data[i] == b1 && data[i + 1] == b2) return i;
			}
			return -1;
		}

		private void OnFrame(byte[] jpegBytes)
		{
			FrameReceived?.Invoke(jpegBytes);

			try
			{
				using var original = SKBitmap.Decode(jpegBytes);
				if (original == null) return;

				using var resized = new SKBitmap(_downscaleWidth, _downscaleHeight, original.ColorType, original.AlphaType);
				using (var canvas = new SKCanvas(resized))
				{
					canvas.Clear(SKColors.Black);
					var dest = new SKRect(0, 0, _downscaleWidth, _downscaleHeight);
					canvas.DrawBitmap(original, dest);
				}

				bool fireMotion = false;
				float ratioForMetrics = 0f;
				int changedForMetrics = 0;
				int pixelCountForMetrics = 0;
				lock (_stateLock)
				{
					if (_previousFrame == null)
					{
						_previousFrame = resized.Copy();
						return;
					}

					int width = resized.Width;
					int height = resized.Height;
					int pixelCount = width * height;
					int changed = 0;

					var currentPixels = resized.Pixels;
					var prevPixels = _previousFrame.Pixels;
					for (int i = 0; i < pixelCount; i++)
					{
						var c = currentPixels[i];
						var p = prevPixels[i];
						int dr = Math.Abs(c.Red - p.Red);
						int dg = Math.Abs(c.Green - p.Green);
						int db = Math.Abs(c.Blue - p.Blue);
						if (dr > _perChannelThreshold || dg > _perChannelThreshold || db > _perChannelThreshold)
						{
							changed++;
						}
					}

					float ratio = (float)changed / pixelCount;
					var now = Environment.TickCount64;
					if (ratio >= _differenceThresholdRatio && (now - _lastDetectionMs) > _cooldownMs)
					{
						_lastDetectionMs = now;
						fireMotion = true;
					}

					_previousFrame.Dispose();
					_previousFrame = resized.Copy();

					ratioForMetrics = ratio;
					changedForMetrics = changed;
					pixelCountForMetrics = pixelCount;
				}

				// emit metrics every frame
				try { Metrics?.Invoke(ratioForMetrics, changedForMetrics, pixelCountForMetrics); } catch { }

				if (fireMotion)
				{
					MotionDetected?.Invoke();
				}
			}
			catch (Exception ex)
			{
				Error?.Invoke(ex.Message);
			}
		}
	}
}


