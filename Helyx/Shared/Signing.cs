using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Helyx.Data;
using LibGit2Sharp;

namespace Helyx.Shared
{
    internal static class Signing
    {
        internal static byte[] PublicKey(ECDsa key)
        {
            var point = key.ExportParameters(false).Q;

            return [.. Field("ecdsa-sha2-nistp256"u8), .. Field("nistp256"u8), .. Field([4, .. point.X!, .. point.Y!])];
        }

        internal static bool Sign(Repository repo, Commit? onto = null)
        {
            if (!ConfigurationHandler.GetConfig().SignCommits)
                return true;

            var stored = ConfigurationHandler.GetSigningKey();

            if (stored == null)
                return false;

            var chain = new List<Commit>();
            var walked = repo.Head.Tip;

            while (walked != null && walked.Sha != onto?.Sha)
            {
                chain.Add(walked);
                walked = onto == null ? null : walked.Parents.FirstOrDefault();
            }

            if (onto != null && walked == null)
                return false;

            if (chain.Count == 0)
                return true;

            chain.Reverse();

            try
            {
                using var key = ECDsa.Create();

                key.ImportPkcs8PrivateKey(stored, out _);

                var publicKey = PublicKey(key);

                Commit? previous = null;

                foreach (var commit in chain)
                {
                    var buffer = Commit.CreateBuffer(commit.Author, commit.Committer, commit.Message, commit.Tree,
                        previous == null ? commit.Parents : commit.Parents.Skip(1).Prepend(previous), false, null);

                    byte[] signed = [.. "SSHSIG"u8, .. Field("git"u8), .. Field([]), .. Field("sha512"u8),
                        .. Field(SHA512.HashData(Encoding.UTF8.GetBytes(buffer)))];

                    var raw = key.SignData(signed, HashAlgorithmName.SHA256);

                    byte[] blob = [.. "SSHSIG"u8, 0, 0, 0, 1, .. Field(publicKey), .. Field("git"u8), .. Field([]), .. Field("sha512"u8),
                        .. Field([.. Field("ecdsa-sha2-nistp256"u8), .. Field([.. Mpint(raw[..32]), .. Mpint(raw[32..])])])];

                    previous = repo.Lookup<Commit>(repo.ObjectDatabase.CreateCommitWithSignature(buffer,
                        "-----BEGIN SSH SIGNATURE-----\n" +
                        string.Join('\n', Convert.ToBase64String(blob).Chunk(70).Select(x => new string(x))) +
                        "\n-----END SSH SIGNATURE-----"));
                }

                repo.Refs.UpdateTarget(repo.Refs.Head.ResolveToDirectReference(), previous!.Id);

                return true;
            }
            catch (Exception ex) when (ex is CryptographicException or LibGit2SharpException)
            {
                return false;
            }
        }

        private static byte[] Field(ReadOnlySpan<byte> data)
        {
            var length = new byte[4];

            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);

            return [.. length, .. data];
        }

        private static byte[] Mpint(byte[] number)
        {
            var trimmed = number.SkipWhile(x => x == 0).ToArray();

            return Field(trimmed.Length > 0 && trimmed[0] >= 0x80 ? [0, .. trimmed] : trimmed);
        }
    }
}
