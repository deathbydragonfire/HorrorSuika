<#
.SYNOPSIS
Bakes chorus + room reverb + body EQ into the Gross SFX so they work on WebGL.

.DESCRIPTION
WebGL ignores AudioMixer effects and AudioFilter components, so the wet chain has to live in the
clips themselves. Every .wav in Assets/sounds/Gross is processed into Assets/sounds/Gross/Baked:

  body EQ (low-mid push, harsh-mid dip)
  -> decorrelated stereo chorus (different taps per ear: wet, wobbly, fleshy width)
  -> convolution room reverb (generated IR: early reflections + dark pink-noise tail, ~0.9 s RT60)
  -> loudness match (every clip's loudest moment hits the same LUFS) + true-peak-safe limiter,
     so the runtime random volume is the only thing that varies level between hits.

The reverb tail is baked in, so pooled AudioSources never cut it off. Re-run after adding clips:
  powershell -ExecutionPolicy Bypass -File Tools/Audio/BakeGrossFx.ps1
Requires ffmpeg on PATH.
#>
param(
    [string]$SourceDir = (Join-Path $PSScriptRoot '..\..\Assets\sounds\Gross'),
    [string]$OutputDir = (Join-Path $PSScriptRoot '..\..\Assets\sounds\Gross\Baked'),
    [double]$ReverbSend = 0.1,
    [double]$TargetLoudnessLufs = -16.0,
    # Sample-peak ceiling; -1.5 keeps inter-sample (true) peaks under -1 dBTP after encoding.
    [double]$PeakCeilingDb = -1.5,
    [switch]$KeepWorkFiles
)

$ErrorActionPreference = 'Stop'
$SampleRate = 48000
$TailSeconds = 1.3

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) {
    throw 'ffmpeg not found on PATH.'
}

$SourceDir = (Resolve-Path $SourceDir).Path
New-Item -ItemType Directory -Force $OutputDir | Out-Null
$OutputDir = (Resolve-Path $OutputDir).Path
$WorkDir = Join-Path ([System.IO.Path]::GetTempPath()) 'horrorsuika-audio-bake'
New-Item -ItemType Directory -Force $WorkDir | Out-Null

function Invoke-Ffmpeg([string[]]$Arguments) {
    # ffmpeg logs to stderr; Windows PowerShell turns that into terminating errors under 'Stop'.
    $ErrorActionPreference = 'Continue'
    $output = & ffmpeg -hide_banner -nostdin @Arguments 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed:`n$($output -join "`n")"
    }
    return $output
}

$Invariant = [System.Globalization.CultureInfo]::InvariantCulture

function Measure-PeakLoudness([string]$Path) {
    $output = Invoke-Ffmpeg @('-nostats', '-i', $Path, '-af', 'ebur128=metadata=1,ametadata=print:key=lavfi.r128.M', '-f', 'null', '-')
    $values = $output | Select-String 'lavfi\.r128\.M=(-?[\d.]+)' | ForEach-Object { [double]::Parse($_.Matches[0].Groups[1].Value, $Invariant) }
    return ($values | Measure-Object -Maximum).Maximum
}

function Measure-SamplePeak([string]$Path) {
    $output = Invoke-Ffmpeg @('-i', $Path, '-af', 'volumedetect', '-f', 'null', '-')
    return [double]::Parse(($output | Select-String 'max_volume: (-?[\d.]+) dB').Matches[0].Groups[1].Value, $Invariant)
}

function Write-NormalizedClip([string]$Source, [string]$Destination, [double]$GainDb) {
    $gainText = $GainDb.ToString('0.00', $Invariant)
    $ceilingText = [Math]::Pow(10, $PeakCeilingDb / 20).ToString('0.0000', $Invariant)
    # Limiting at 4x rate catches the inter-sample peaks a 48 kHz limiter misses (true-peak safe).
    $filter = "aresample=$($SampleRate * 4),volume=${gainText}dB,alimiter=limit=${ceilingText}:attack=1:release=60:level=disabled,aresample=${SampleRate},areverse,afade=t=in:d=0.02,areverse"
    Invoke-Ffmpeg @('-y', '-i', $Source, '-af', $filter, '-c:a', 'pcm_s16le', '-ar', "$SampleRate", $Destination) | Out-Null
}

# Room impulse response: a sparse early-reflection cluster (different per ear) followed by a
# pre-delayed, exponentially decaying, band-limited pink-noise tail. Dark on purpose: wet walls.
$irPath = Join-Path $WorkDir 'room_ir.wav'
$irLength = 1.4
$irGraph = @(
    "aevalsrc='if(eq(n,0),1,0)|if(eq(n,0),1,0)':s=${SampleRate}:d=${irLength}[imp]",
    "[imp]channelsplit=channel_layout=stereo[il][ir]",
    "[il]aecho=1:1:7|13|19|29|41:0.55|0.42|0.36|0.28|0.2[el]",
    "[ir]aecho=1:1:9|16|23|34|47:0.5|0.44|0.33|0.26|0.18[er]",
    "[el][er]join=inputs=2:channel_layout=stereo,lowpass=f=6000[early]",
    "anoisesrc=d=${irLength}:c=pink:r=${SampleRate}:a=0.6:seed=11:n=64[nl]",
    "anoisesrc=d=${irLength}:c=pink:r=${SampleRate}:a=0.6:seed=29:n=64[nr]",
    "[nl][nr]join=inputs=2:channel_layout=stereo,highpass=f=160,lowpass=f=3600,volume='exp(-7.6*t)':eval=frame,adelay=18|22,atrim=0:${irLength}[tail]",
    "[early][tail]amix=inputs=2:weights='0.6 0.45':normalize=0"
) -join ';'
Invoke-Ffmpeg @('-y', '-filter_complex', $irGraph, '-c:a', 'pcm_f32le', $irPath) | Out-Null

$clips = Get-ChildItem -Path $SourceDir -Filter *.wav -File
if ($clips.Count -eq 0) {
    throw "No .wav files in $SourceDir"
}

foreach ($clip in $clips) {
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($clip.Name)
    $wetPath = Join-Path $WorkDir "$stem.wet.wav"
    $outPath = Join-Path $OutputDir "${stem}_fx.wav"

    $graph = @(
        "[0:a:0]aresample=${SampleRate},aformat=sample_fmts=fltp:channel_layouts=stereo,silenceremove=start_periods=1:start_threshold=-50dB,afade=t=in:d=0.003,highpass=f=40,equalizer=f=210:t=o:w=1.3:g=3.5,equalizer=f=3300:t=o:w=1.4:g=-2.5,apad=pad_dur=${TailSeconds}[pre]",
        "[pre]channelsplit=channel_layout=stereo[l][r]",
        "[l]chorus=0.7:0.9:21|36|52:0.42|0.34|0.27:0.23|0.41|0.61:1.9|2.5|1.5[lc]",
        "[r]chorus=0.7:0.9:26|33|57:0.42|0.34|0.27:0.29|0.37|0.53:2.2|1.7|2.3[rc]",
        "[lc][rc]join=inputs=2:channel_layout=stereo,asplit=2[dry][send]",
        # irnorm=-1 keeps the IR's own energy (ffmpeg 7 otherwise normalises it ~40 dB down),
        # so ReverbSend alone sets the wet level: 0.1 sits the room roughly 9 dB under the dry hit.
        "[send][1:a]afir=irnorm=-1[verb]",
        "[dry][verb]amix=inputs=2:weights='1 ${ReverbSend}':normalize=0,areverse,silenceremove=start_periods=1:start_threshold=-62dB,areverse"
    ) -join ';'
    Invoke-Ffmpeg @('-y', '-i', $clip.FullName, '-i', $irPath, '-filter_complex', $graph, '-c:a', 'pcm_f32le', $wetPath) | Out-Null

    # Level-match on the loudest 400 ms (max momentary LUFS): that is how loud the hit is heard.
    # File-wide averages count the silent reverb tail and let long, sparse clips read as quiet.
    $sourceLoudness = Measure-PeakLoudness $wetPath
    $gain = $TargetLoudnessLufs - $sourceLoudness
    Write-NormalizedClip $wetPath $outPath $gain

    # Boosted clips lose some loudness to the limiter; a few correction passes close the gap.
    for ($pass = 0; $pass -lt 4; $pass++) {
        $residual = $TargetLoudnessLufs - (Measure-PeakLoudness $outPath)
        if ([Math]::Abs($residual) -le 0.2) {
            break
        }

        $gain += $residual
        Write-NormalizedClip $wetPath $outPath $gain
    }

    $finalLoudness = Measure-PeakLoudness $outPath
    $limiting = [Math]::Max(0, $gain + (Measure-SamplePeak $wetPath) - $PeakCeilingDb)
    Write-Output ("{0,-52} {1,6:0.0} -> {2,6:0.0} LUFS  gain {3,5:0.0} dB  limiting {4,4:0.0} dB" -f $clip.Name, $sourceLoudness, $finalLoudness, $gain, $limiting)
}

if ($KeepWorkFiles) {
    Write-Output "Intermediate files kept in $WorkDir"
} else {
    Remove-Item -Recurse -Force $WorkDir
}
Write-Output "Baked $($clips.Count) clips into $OutputDir"
