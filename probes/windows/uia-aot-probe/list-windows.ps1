Get-Process | Where-Object { $_.MainWindowHandle -ne 0 } | ForEach-Object { "{0} pid={1} title={2}" -f $_.ProcessName, $_.Id, $_.MainWindowTitle }
