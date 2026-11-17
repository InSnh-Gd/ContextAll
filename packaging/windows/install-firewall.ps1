[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = 'Stop'

$rules = @(
    @{
        Name        = 'ContextAll QUIC'
        Description = 'Inbound QUIC for ContextAll paired sessions'
        Port        = 42461
    },
    @{
        Name        = 'ContextAll Beacon'
        Description = 'Inbound UDP beacon for ContextAll discovery'
        Port        = 42462
    }
)

foreach ($rule in $rules) {
    if (-not $PSCmdlet.ShouldProcess("UDP $($rule.Port) inbound Private", "Create firewall rule '$($rule.Name)'")) {
        Write-Output "Would create inbound UDP $($rule.Port) allow rule '$($rule.Name)' on Private profile"
        continue
    }

    $existing = Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Output "Rule already exists: $($rule.Name) UDP $($rule.Port) inbound allow Private"
        continue
    }

    New-NetFirewallRule `
        -DisplayName $rule.Name `
        -Description $rule.Description `
        -Direction Inbound `
        -Protocol UDP `
        -LocalPort $rule.Port `
        -Action Allow `
        -Profile Private `
        | Out-Null
    Write-Output "Created rule: $($rule.Name) UDP $($rule.Port) inbound allow Private"
}
