# Fix My Encoding

A simple tool for converting the encoding of text files and line-ending characters to the UTF-8 standard, using LF (instead of the Windows default).

_(Be careful with your files — make sure you have a backup.)_

## Download

Ready-to-use builds for Windows, Linux and macOS (x64 and ARM64) are available in [Releases](../../releases). No .NET installation is required.

## Build from source

Requires .NET 10 SDK.

dotnet publish FixMyEncoding.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

Replace `linux-x64` with `win-x64`, `win-arm64`, `linux-arm64`, `osx-x64`, or `osx-arm64`.

# Super mega GIF for no reason

![Super Herta Spin GIF, really important](super-spin-gif-really-important.gif)

_(Super Herta Spin Animation by [@Seseren_kr](https://x.com))_
