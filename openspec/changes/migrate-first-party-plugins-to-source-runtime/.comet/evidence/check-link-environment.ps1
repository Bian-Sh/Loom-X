$ErrorActionPreference = 'Stop'
$root = Join-Path 'D:/AppData/Github/Loom-X/.worktrees/add-source-plugin-runtime-and-bridge/.comet/evidence' ('check-link-probe-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
'Identity='+[Security.Principal.WindowsIdentity]::GetCurrent().Name
'Is64Bit='+[Environment]::Is64BitProcess
'工作目录='+[Environment]::CurrentDirectory
$failures=0
foreach($location in @($root,(Join-Path ([IO.Path]::GetTempPath()) ('loomx-check-link-'+[Guid]::NewGuid().ToString('N'))))){
    $outside=Join-Path $location 'outside'
    New-Item -ItemType Directory -Path $outside -Force|Out-Null
    $file=Join-Path $outside 'data.db'
    [IO.File]::WriteAllText($file,'合成测试')
    try{[IO.File]::CreateSymbolicLink((Join-Path $location 'link.db'),$file)|Out-Null;'FILE_SYMLINK_OK '+$location}catch{$failures++;'FILE_SYMLINK_FAIL '+$_.Exception.GetType().Name}
    try{[IO.Directory]::CreateSymbolicLink((Join-Path $location 'linked'),$outside)|Out-Null;'DIR_SYMLINK_OK '+$location}catch{$failures++;'DIR_SYMLINK_FAIL '+$_.Exception.GetType().Name}
    $psi=[Diagnostics.ProcessStartInfo]::new('cmd.exe')
    $psi.UseShellExecute=$false;$psi.CreateNoWindow=$true;$psi.RedirectStandardOutput=$true;$psi.RedirectStandardError=$true
    foreach($a in @('/c','mklink','/J',(Join-Path $location 'junction'),$outside)){$psi.ArgumentList.Add($a)}
    $proc=[Diagnostics.Process]::Start($psi);$proc.WaitForExit();$proc.StandardOutput.ReadToEnd();$proc.StandardError.ReadToEnd();'JUNCTION_EXIT='+$proc.ExitCode
    if($proc.ExitCode -ne 0){$failures++}
}
exit $failures