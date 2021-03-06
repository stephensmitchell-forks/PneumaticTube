﻿using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dropbox.Api;
using Dropbox.Api.Files;

namespace PneumaticTube
{
	internal static class DropboxClientExtensions
	{
		public const int ChunkSize = 128 * 1024;
		public const int ChunkedThreshold = 150 * 1024 * 1024;

		private static string CombinePath(string folder, string fileName) 
		{
			// We can't use Path.Combine here because we'll end up with the Windows separator ("\") and 
			// we need the forward slash ("/")

			if (folder == "/") 
			{
				return $"/{fileName}";
			}
			
			return $"{folder}/{fileName}";
		}

		public static async Task<FileMetadata> Upload(this DropboxClient client, string folder, string fileName, Stream fs)
		{
			var fullDestinationPath = CombinePath(folder, fileName);

			return await client.Files.UploadAsync(fullDestinationPath, WriteMode.Overwrite.Instance, body: fs);
		}

		public static async Task<FileMetadata> UploadChunked(this DropboxClient client, 
			string folder, string fileName, Stream fs, CancellationToken cancellationToken, IProgress<long> progress)
		{
			int chunks = (int)Math.Ceiling((double)fs.Length / ChunkSize);

			byte[] buffer = new byte[ChunkSize];
			string sessionId = null;

			FileMetadata resultMetadata = null;
			var fullDestinationPath = CombinePath(folder, fileName);

			for (var i = 0; i < chunks; i++)
			{
				if(cancellationToken.IsCancellationRequested)
				{
					throw new OperationCanceledException(cancellationToken);
				}

				var bytesRead = fs.Read(buffer, 0, ChunkSize);

				using(var memStream = new MemoryStream(buffer, 0, bytesRead))
				{
					if(i == 0)
					{
						var result = await client.Files.UploadSessionStartAsync(body: memStream);
						sessionId = result.SessionId;
					}
					else
					{
						UploadSessionCursor cursor = new UploadSessionCursor(sessionId, (ulong)(ChunkSize * i));

						if(i == chunks - 1)
						{
							resultMetadata = await client.Files.UploadSessionFinishAsync(cursor, new CommitInfo(fullDestinationPath, WriteMode.Overwrite.Instance), memStream);

							if(!cancellationToken.IsCancellationRequested)
							{
								progress.Report(fs.Length);
							}
						}
						else
						{
							await client.Files.UploadSessionAppendV2Async(cursor, body: memStream);
							if(!cancellationToken.IsCancellationRequested)
							{
								progress.Report(i * ChunkSize);
							}
						}
					}
				}
			}

			return resultMetadata;
		}
	}
}