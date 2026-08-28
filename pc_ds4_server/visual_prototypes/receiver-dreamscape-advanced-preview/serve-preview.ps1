param(
    [Parameter(Mandatory = $true)][string]$Root,
    [int]$Port = 8765
)

$resolvedRoot = [System.IO.Path]::GetFullPath($Root)
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, $Port)
$listener.Start()
try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $stream = $client.GetStream()
            $reader = [System.IO.StreamReader]::new($stream, [System.Text.Encoding]::ASCII, $false, 1024, $true)
            $requestLine = $reader.ReadLine()
            while ($reader.ReadLine()) { }
            $requestTarget = ($requestLine -split ' ')[1]
            $requestPath = ($requestTarget -split '\?')[0]
            $relative = [System.Uri]::UnescapeDataString($requestPath.TrimStart('/'))
            if ([string]::IsNullOrWhiteSpace($relative)) { $relative = 'preview.html' }
            $path = [System.IO.Path]::GetFullPath([System.IO.Path]::Combine($resolvedRoot, $relative))
            if (-not $path.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase) -or
                -not [System.IO.File]::Exists($path)) {
                $notFound = [System.Text.Encoding]::UTF8.GetBytes('Not Found')
                $header = [System.Text.Encoding]::ASCII.GetBytes("HTTP/1.1 404 Not Found`r`nContent-Length: $($notFound.Length)`r`nConnection: close`r`n`r`n")
                $stream.Write($header, 0, $header.Length)
                $stream.Write($notFound, 0, $notFound.Length)
                $client.Close()
                continue
            }
            $contentType = switch ([System.IO.Path]::GetExtension($path).ToLowerInvariant()) {
                '.html' { 'text/html; charset=utf-8' }
                '.css'  { 'text/css; charset=utf-8' }
                '.js'   { 'text/javascript; charset=utf-8' }
                '.png'  { 'image/png' }
                '.svg'  { 'image/svg+xml' }
                default { 'application/octet-stream' }
            }
            $bytes = [System.IO.File]::ReadAllBytes($path)
            $header = [System.Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: $contentType`r`nContent-Length: $($bytes.Length)`r`nCache-Control: no-store`r`nConnection: close`r`n`r`n")
            $stream.Write($header, 0, $header.Length)
            $stream.Write($bytes, 0, $bytes.Length)
            $stream.Flush()
            $client.Close()
        } catch {
            $client.Close()
        }
    }
} finally {
    $listener.Stop()
}
