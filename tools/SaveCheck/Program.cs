using System.Diagnostics;
using System.Security.Cryptography;
using NHSE.Core;

if (args.Length != 1 || !Directory.Exists(args[0]))
{
    Console.WriteLine("usage: SaveCheck <copy of an ACNH save folder>");
    return 2;
}

// 1. The managed AES-CTR must match .NET's AES exactly.
var random = new Random(20261022);
for (int i = 0; i < 500; i++)
{
    var key = new byte[16];
    var counter = new byte[16];
    var data = new byte[random.Next(1, 4096)];
    random.NextBytes(key);
    random.NextBytes(counter);
    random.NextBytes(data);
    if (i == 0)
        Array.Fill(counter, (byte)0xFF); // counter wrap-around

    if (!AesCtr.Encrypt(data, key, counter).AsSpan().SequenceEqual(ReferenceCtr(data, key, counter)))
    {
        Console.WriteLine($"AES mismatch in case {i}");
        return 1;
    }
}
Console.WriteLine("AES: 500 random cases match .NET");

// 2. Load the copy in memory and check every file hash.
var folder = args[0];
var provider = new MemorySaveFileProvider(Directory
    .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
    .Select(f => KeyValuePair.Create(Path.GetRelativePath(folder, f), File.ReadAllBytes(f))));

var timer = Stopwatch.StartNew();
var save = new HorizonSave(provider);
Console.WriteLine($"Loaded in {timer.ElapsedMilliseconds} ms, revision {save.Main.Info}");
Console.WriteLine($"Sizes valid: {save.ValidateSizes()}, invalid hashes: {save.GetInvalidHashes().Count()}");
foreach (var player in save.Players)
{
    var p = player.Personal;
    Console.WriteLine($"  {p.PlayerName} from {p.TownName}: wallet {p.Wallet.Value}, bank {p.Bank.Value}, " +
                      $"miles {p.NookMiles.Value} (total {p.TotalNookMiles.Value})");
}

// 3. Change the bank by one bell, save, load the result again and compare.
var personal = save.Players[0].Personal;
uint before = personal.Bank.Value;
var bank = personal.Bank;
bank.Value = before == 999_999_999 ? before - 1 : before + 1;
personal.Bank = bank;

timer.Restart();
save.Save((uint)Environment.TickCount);
Console.WriteLine($"Saved in {timer.ElapsedMilliseconds} ms, rewritten files: {provider.Changed.Count}");

var reloaded = new HorizonSave(new MemorySaveFileProvider(provider.Files));
Console.WriteLine($"Reloaded: invalid hashes {reloaded.GetInvalidHashes().Count()}, " +
                  $"bank {before} -> {reloaded.Players[0].Personal.Bank.Value}");
return reloaded.GetInvalidHashes().Any() ? 1 : 0;

static byte[] ReferenceCtr(byte[] data, byte[] key, byte[] counter)
{
    using var aes = Aes.Create();
    aes.Key = key;
    var ctr = (byte[])counter.Clone();
    var block = new byte[16];
    var result = (byte[])data.Clone();
    for (int offset = 0; offset < result.Length; offset += 16)
    {
        aes.EncryptEcb(ctr, block, PaddingMode.None);
        for (int i = 0; i < 16 && offset + i < result.Length; i++)
            result[offset + i] ^= block[i];
        for (int i = 15; i >= 0 && ++ctr[i] == 0; i--) { }
    }
    return result;
}
