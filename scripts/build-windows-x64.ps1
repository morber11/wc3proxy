Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
# repo root is the parent of the scripts directory
$RepoRoot = Split-Path -Parent $ScriptDir
if (-not (Test-Path $RepoRoot)) { $RepoRoot = Get-Location }

Push-Location $RepoRoot

Write-Host "Preparing publish (embedding CLI into GUI)..."

$Configuration = 'Release'
$Rid = 'win-x64'
$TmpPublish = Join-Path -Path $RepoRoot -ChildPath 'tmp\cli_publish'
$Dist = Join-Path -Path $RepoRoot -ChildPath 'dist'

Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $TmpPublish, $Dist

foreach ($d in @($TmpPublish, $Dist)) {
	if (-not (Test-Path $d)) {
		New-Item -ItemType Directory -Path $d | Out-Null
	}
}

Write-Host "Publishing CLI to temporary folder..."
dotnet publish .\cli\wc3proxy.csproj -c $Configuration -r $Rid -p:PublishSingleFile=true -p:SelfContained=true -p:PublishReadyToRun=false -o $TmpPublish

$publishedCli = Get-ChildItem -Path $TmpPublish -Filter "*.exe" -File | Where-Object { $_.Name -ieq 'wc3proxy.exe' } | Select-Object -First 1
if ($null -eq $publishedCli) {
	# Fallback: look for an exe whose base filename is exactly 'wc3proxy'
	$publishedCli = Get-ChildItem -Path $TmpPublish -Filter "*.exe" -File | Where-Object { $_.BaseName -ieq 'wc3proxy' } | Select-Object -First 1
}
if ($null -eq $publishedCli) { throw "Published CLI executable not found in $TmpPublish" }

Write-Host "Copying CLI into gui/avalonia/EmbeddedCli so it will be embedded in the GUI assembly..."
$embeddedDir = Join-Path -Path $RepoRoot -ChildPath 'gui\avalonia\EmbeddedCli'
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $embeddedDir
New-Item -ItemType Directory -Path $embeddedDir | Out-Null
Copy-Item -Path $publishedCli.FullName -Destination (Join-Path -Path $embeddedDir -ChildPath $publishedCli.Name) -Force

# force clean build
$guiBin = Join-Path -Path $RepoRoot -ChildPath 'gui\avalonia\bin'
$guiObj = Join-Path -Path $RepoRoot -ChildPath 'gui\avalonia\obj'
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $guiBin, $guiObj

Write-Host "Publishing Avalonia GUI (the CLI will be embedded as an EmbeddedResource)..."

$guiExeName = 'wc3proxy-gui.exe'
$distExe = Join-Path -Path $Dist -ChildPath $guiExeName

function Stop-ProcessesByName {
	param([string[]]$names)
	foreach ($n in $names) {
		$procs = Get-Process -Name $n -ErrorAction SilentlyContinue
		if ($procs) {
			Write-Host "Stopping processes named '$n': $(( $procs | ForEach-Object { $_.Id } ) -join ', ')"
			foreach ($p in $procs) {
				try {
					Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
					try { Wait-Process -Id $p.Id -Timeout 5 -ErrorAction SilentlyContinue } catch { }
					$still = Get-Process -Id $p.Id -ErrorAction SilentlyContinue
					if ($still) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
				}
				catch {
					Write-Warning "Failed to stop process Id $($p.Id): $_"
				}
			}
		}
	}
}

if (Test-Path $distExe) {
	Write-Host "Target executable exists: $distExe - attempting to remove to avoid publish lock..."
	try {
		Remove-Item -Force -ErrorAction Stop $distExe
		Write-Host "Removed existing file: $distExe"
	}
	catch {
		Write-Host "Failed to remove $distExe, attempting to stop running processes that may lock it..."

		Stop-ProcessesByName -names @('wc3proxy-gui','wc3proxy')

		try {
			Remove-Item -Force -ErrorAction Stop $distExe
			Write-Host "Removed existing file after stopping processes: $distExe"
		}
		catch {
			throw "The output file '$distExe' is locked by another process and could not be removed. Ensure no running instances are using it and retry."
		}
	}
}

dotnet publish .\gui\avalonia\wc3proxy-gui.csproj -c $Configuration -r $Rid -p:PublishSingleFile=true -p:SelfContained=true -p:PublishReadyToRun=true -p:PublishSingleFile=true -o $Dist

Write-Host "Cleaning temporary files..."
Remove-Item -Recurse -Force -ErrorAction SilentlyContinue $embeddedDir, $TmpPublish

Write-Host "Publish complete. Artifacts in: $Dist"

Pop-Location
