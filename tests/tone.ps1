# Plays a quiet 440 Hz test tone from its own powershell process.
#   -Seconds  length of each burst   -Bursts  number of bursts   -Gap  silence between bursts
param(
    [double]$Seconds = 3,
    [int]$Bursts = 1,
    [double]$Gap = 0
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @"
using System;
using System.IO;
public static class ToneGen {
    public static void Write(string path, double seconds, double amplitude) {
        int rate = 44100, n = (int)(seconds * rate);
        using (var w = new BinaryWriter(File.Create(path))) {
            w.Write(new[]{'R','I','F','F'}); w.Write(36 + n * 2); w.Write(new[]{'W','A','V','E'});
            w.Write(new[]{'f','m','t',' '}); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(new[]{'d','a','t','a'}); w.Write(n * 2);
            for (int i = 0; i < n; i++) w.Write((short)(Math.Sin(2 * Math.PI * 440 * i / rate) * amplitude * 32767));
        }
    }
}
"@

$wav = Join-Path $env:TEMP ("ss_tone_{0}.wav" -f $PID)
try {
    [ToneGen]::Write($wav, $Seconds, 0.1)
    $player = New-Object System.Media.SoundPlayer $wav
    $player.Load()
    for ($b = 0; $b -lt $Bursts; $b++) {
        if ($b -gt 0 -and $Gap -gt 0) { Start-Sleep -Milliseconds ([int]($Gap * 1000)) }
        $player.PlaySync()
    }
    Start-Sleep -Milliseconds 300
} finally {
    Remove-Item $wav -ErrorAction SilentlyContinue
}
