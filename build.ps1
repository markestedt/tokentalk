#!/usr/bin/env pwsh
Write-Host "Building TokenTalk for Windows (static binary)..." -ForegroundColor Cyan

$env:CGO_ENABLED = "1"
go build -ldflags "-s -w -extldflags=-static" -o tokentalk.exe .

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Build successful! tokentalk.exe created." -ForegroundColor Green
    Write-Host "This exe should work on any Windows x64 machine."
} else {
    Write-Host ""
    Write-Host "Build failed!" -ForegroundColor Red
    Write-Host "Make sure you have a C compiler installed (TDM-GCC or MinGW-w64)."
    exit $LASTEXITCODE
}
