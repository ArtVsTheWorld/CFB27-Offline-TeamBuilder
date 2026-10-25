using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace OfflineTeamBuilder.Repair;

internal static class AssetValidation
{
	private sealed class ExactInput : Stream
	{
		private readonly Stream source;

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => source.Length;

		public override long Position
		{
			get
			{
				return source.Position;
			}
			set
			{
				throw new NotSupportedException();
			}
		}

		public ExactInput(Stream source)
		{
			this.source = source;
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			return source.Read(buffer, offset, Math.Min(count, 1));
		}

		public override void Flush()
		{
		}

		public override long Seek(long o, SeekOrigin k)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long n)
		{
			throw new NotSupportedException();
		}

		public override void Write(byte[] b, int o, int n)
		{
			throw new NotSupportedException();
		}
	}

	private static readonly uint[] CrcTable = BuildCrcTable();

	private static uint[] BuildCrcTable()
	{
		uint[] array = new uint[256];
		for (uint num = 0u; num < 256; num++)
		{
			uint num2 = num;
			for (int i = 0; i < 8; i++)
			{
				num2 = (((num2 & 1) != 0) ? (0xEDB88320u ^ (num2 >> 1)) : (num2 >> 1));
			}
			array[num] = num2;
		}
		return array;
	}

	private static uint BE(byte[] b, int o)
	{
		return (uint)((b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]);
	}

	private static uint LE(byte[] b, int o)
	{
		return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24));
	}

	private static byte[] Read(Stream s, int count)
	{
		byte[] array = new byte[count];
		int i;
		int num;
		for (i = 0; i < count; i += num)
		{
			if ((num = s.Read(array, i, count - i)) <= 0)
			{
				break;
			}
		}
		if (i != count)
		{
			throw new InvalidDataException("Truncated asset.");
		}
		return array;
	}

	public static string Validate(string file, string kind, RepairOptions options)
	{
		using FileStream fileStream = File.OpenRead(file);
		if (fileStream.Length < 8 || fileStream.Length > options.MaximumUploadBytes)
		{
			throw new InvalidDataException("Empty, too short, or oversized artifact.");
		}
		byte[] array = Read(fileStream, (int)Math.Min(148L, fileStream.Length));
		fileStream.Position = 0L;
		bool flag = kind == "Logo1Data" || kind == "Logo2Data";
		if (Encoding.ASCII.GetString(array, 0, 8) == "FBCHUNKS")
		{
			if (array.Length < 18)
			{
				throw new InvalidDataException("Truncated FBCHUNKS header.");
			}
			long num = LE(array, 10);
			long num2 = LE(array, 14);
			long num3 = checked(18 + num);
			if (num3 >= fileStream.Length || num2 != fileStream.Length - num3)
			{
				throw new InvalidDataException("FBCHUNKS allocation/header bounds do not match the file.");
			}
			if (array[8] != 1 || array[9] != 0)
			{
				throw new InvalidDataException("Unrecognized FBCHUNKS container version; retained for diagnosis.");
			}
			return "FBCHUNKS v1; zlib checksum valid; inflated=" + ValidateZlib(fileStream, num3, options.MaximumInflatedBytes, allowZeroPadding: true) + "; native render interpretation NOT verified";
		}
		if (array[0] == 137 && Encoding.ASCII.GetString(array, 1, 3) == "PNG")
		{
			if (!flag)
			{
				throw new InvalidDataException("A standalone PNG is not a RenderData package.");
			}
			ValidatePng(fileStream);
			return "PNG chunk CRCs and framing valid; native initialization NOT verified";
		}
		if (Encoding.ASCII.GetString(array, 0, 4) == "DDS ")
		{
			if (!flag)
			{
				throw new InvalidDataException("A standalone DDS is not a RenderData package.");
			}
			ValidateDds(fileStream, array);
			return "DDS header/dimensions/payload bounds valid; native initialization NOT verified";
		}
		if ((array[0] & 0xF) == 8 && ((array[0] << 8) + array[1]) % 31 == 0)
		{
			return "zlib checksum valid; inflated=" + ValidateZlib(fileStream, 0L, options.MaximumInflatedBytes, allowZeroPadding: false) + "; native render interpretation NOT verified";
		}
		throw new InvalidDataException("Unrecognized artifact framing. Bytes quarantined, not acknowledged as valid. A native upload sample is needed; do not blindly relabel/recompress it.");
	}

	private static long ValidateZlib(Stream s, long start, long maximum, bool allowZeroPadding)
	{
		s.Position = start;
		byte[] array = Read(s, 2);
		if ((array[0] & 0xF) != 8 || array[0] >> 4 > 7 || ((array[0] << 8) + array[1]) % 31 != 0 || (array[1] & 0x20) != 0)
		{
			throw new InvalidDataException("Unsupported/invalid zlib wrapper.");
		}
		uint num = 1u;
		uint num2 = 0u;
		long num3 = 0L;
		byte[] array2 = new byte[32768];
		using (ExactInput stream = new ExactInput(s))
		{
			using DeflateStream deflateStream = new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true);
			int num4;
			while ((num4 = deflateStream.Read(array2, 0, array2.Length)) > 0)
			{
				num3 += num4;
				if (num3 > maximum)
				{
					throw new InvalidDataException("Inflated asset exceeds the configured limit.");
				}
				for (int i = 0; i < num4; i++)
				{
					num = (num + array2[i]) % 65521;
					num2 = (num2 + num) % 65521;
				}
			}
		}
		uint num5 = BE(Read(s, 4), 0);
		if (num3 == 0L || num5 != ((num2 << 16) | num))
		{
			throw new InvalidDataException("Empty/corrupt zlib payload or Adler-32 mismatch.");
		}
		if (!allowZeroPadding && s.Position != s.Length)
		{
			throw new InvalidDataException("Unexpected data after the zlib stream.");
		}
		if (allowZeroPadding)
		{
			int num6;
			while ((num6 = s.Read(array2, 0, array2.Length)) > 0)
			{
				for (int j = 0; j < num6; j++)
				{
					if (array2[j] != 0)
					{
						throw new InvalidDataException("Nonzero trailing FBCHUNKS data is not supported by this validator.");
					}
				}
			}
		}
		return num3;
	}

	private static void ValidatePng(Stream s)
	{
		byte[] array = Read(s, 8);
		byte[] array2 = new byte[8] { 137, 80, 78, 71, 13, 10, 26, 10 };
		for (int i = 0; i < 8; i++)
		{
			if (array[i] != array2[i])
			{
				throw new InvalidDataException("Invalid PNG signature.");
			}
		}
		bool flag = true;
		bool flag2 = false;
		bool flag3 = false;
		int num = 0;
		while (s.Position < s.Length)
		{
			if (++num > 65536)
			{
				throw new InvalidDataException("Too many PNG chunks.");
			}
			byte[] array3 = Read(s, 8);
			uint num2 = BE(array3, 0);
			string text = Encoding.ASCII.GetString(array3, 4, 4);
			if (num2 > s.Length - s.Position - 4)
			{
				throw new InvalidDataException("Truncated PNG chunk.");
			}
			if (flag && (text != "IHDR" || num2 != 13))
			{
				throw new InvalidDataException("Missing PNG IHDR.");
			}
			uint num3 = uint.MaxValue;
			for (int j = 4; j < 8; j++)
			{
				num3 = CrcTable[(num3 ^ array3[j]) & 0xFF] ^ (num3 >> 8);
			}
			long num4 = num2;
			byte[] array4 = new byte[65536];
			while (num4 > 0)
			{
				int num5 = s.Read(array4, 0, (int)Math.Min(array4.Length, num4));
				if (num5 == 0)
				{
					throw new InvalidDataException("Truncated PNG data.");
				}
				if (flag)
				{
					uint num6 = BE(array4, 0);
					uint num7 = BE(array4, 4);
					if (num6 == 0 || num7 == 0 || num6 > 16384 || num7 > 16384)
					{
						throw new InvalidDataException("PNG dimensions exceed supported bounds.");
					}
				}
				for (int k = 0; k < num5; k++)
				{
					num3 = CrcTable[(num3 ^ array4[k]) & 0xFF] ^ (num3 >> 8);
				}
				num4 -= num5;
			}
			if ((num3 ^ 0xFFFFFFFFu) != BE(Read(s, 4), 0))
			{
				throw new InvalidDataException("PNG CRC mismatch.");
			}
			flag = false;
			if (text == "IDAT")
			{
				flag2 = true;
			}
			if (text == "IEND")
			{
				if (num2 != 0 || s.Position != s.Length)
				{
					throw new InvalidDataException("Invalid PNG ending.");
				}
				flag3 = true;
				break;
			}
		}
		if (!flag2 || !flag3)
		{
			throw new InvalidDataException("Incomplete PNG image.");
		}
	}

	private static void ValidateDds(Stream s, byte[] h)
	{
		if (h.Length < 128 || LE(h, 4) != 124 || LE(h, 76) != 32)
		{
			throw new InvalidDataException("Invalid DDS header.");
		}
		uint num = LE(h, 16);
		uint num2 = LE(h, 12);
		uint num3 = Math.Max(1u, LE(h, 28));
		if (num == 0 || num2 == 0 || num > 16384 || num2 > 16384 || num3 > 15 || LE(h, 24) > 1 || LE(h, 112) != 0)
		{
			throw new InvalidDataException("Unsupported DDS dimensions, volume, cube, or mip count.");
		}
		int num4;
		switch (Encoding.ASCII.GetString(h, 84, 4))
		{
		case "DXT1":
		case "ATI1":
		case "BC4U":
			num4 = 8;
			break;
		case "DXT3":
		case "DXT5":
		case "ATI2":
		case "BC5U":
			num4 = 16;
			break;
		default:
			throw new InvalidDataException("DDS encoding is not yet validated by this test build; bytes retained for diagnosis.");
		}
		long num5 = 128L;
		for (uint num6 = 0u; num6 < num3; num6++)
		{
			checked
			{
				num5 += unchecked((long)Math.Max(1u, checked(num + 3) / 4)) * unchecked((long)Math.Max(1u, checked(num2 + 3) / 4)) * num4;
			}
			num = Math.Max(1u, num / 2);
			num2 = Math.Max(1u, num2 / 2);
		}
		if (s.Length != num5)
		{
			throw new InvalidDataException("DDS mip payload length mismatch.");
		}
	}
}
