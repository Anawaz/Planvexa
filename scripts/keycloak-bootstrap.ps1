[CmdletBinding()]
param(
    [string]$BaseUrl = $(if ($env:KEYCLOAK_URL) { $env:KEYCLOAK_URL } else { 'http://localhost:8081' }),
    [string]$AdminUser = $(if ($env:KEYCLOAK_ADMIN_USER) { $env:KEYCLOAK_ADMIN_USER } else { 'admin' }),
    [string]$AdminPassword = $(if ($env:KEYCLOAK_ADMIN_PASSWORD) { $env:KEYCLOAK_ADMIN_PASSWORD } else { 'admin' }),
    [string]$Realm = $(if ($env:KEYCLOAK_REALM) { $env:KEYCLOAK_REALM } else { 'planvexa' }),
    [string]$WebClientId = $(if ($env:KEYCLOAK_WEB_CLIENT_ID) { $env:KEYCLOAK_WEB_CLIENT_ID } else { 'planvexa-web' }),
    [string]$ApiClientId = $(if ($env:KEYCLOAK_API_CLIENT_ID) { $env:KEYCLOAK_API_CLIENT_ID } else { 'planvexa-api' }),
    [string]$WebOrigin = $(if ($env:PLANVEXA_WEB_URL) { $env:PLANVEXA_WEB_URL } else { 'http://localhost:3000' }),

    # The account matching the API's first-run bootstrap admin (Bootstrap:AdminSubject /
    # Bootstrap:AdminEmail -- see PlanvexaBootstrap). Created on every environment: the API seeds the
    # application-side user and workspace, and this seeds the identity it signs in with. Keep the
    # subject in sync with the API's configuration or the two halves will not join up.
    [string]$BootstrapAdminSubject = $(if ($env:BOOTSTRAP_ADMIN_SUBJECT) { $env:BOOTSTRAP_ADMIN_SUBJECT } else { 'planvexa-admin' }),
    [string]$BootstrapAdminEmail = $(if ($env:BOOTSTRAP_ADMIN_EMAIL) { $env:BOOTSTRAP_ADMIN_EMAIL } else { 'admin@planvexa.local' }),
    [string]$BootstrapAdminPassword = $env:BOOTSTRAP_ADMIN_PASSWORD,

    # The four fixed owner/admin/member/guest development logins. On by default so dev-up and the
    # AppHost are unchanged; pass -IncludeDevelopmentUsers:$false (or PLANVEXA_SEED_DEV_USERS=false)
    # in production so the same script runs there without creating well-known accounts.
    [bool]$IncludeDevelopmentUsers = $(if ($env:PLANVEXA_SEED_DEV_USERS) { [System.Convert]::ToBoolean($env:PLANVEXA_SEED_DEV_USERS) } else { $true })
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')

function Write-Step([string]$Message) {
    Write-Host "[keycloak-bootstrap] $Message"
}

function Wait-Keycloak {
    $deadline = (Get-Date).AddMinutes(3)
    do {
        try {
            Invoke-RestMethod -Method Get -Uri "$BaseUrl/realms/master" -TimeoutSec 5 | Out-Null
            return
        } catch {
            Start-Sleep -Seconds 2
        }
    } while ((Get-Date) -lt $deadline)

    throw "Keycloak at $BaseUrl did not become ready within 3 minutes."
}

function Get-AdminToken {
    $body = @{
        grant_type = 'password'
        client_id = 'admin-cli'
        username = $AdminUser
        password = $AdminPassword
    }
    $token = Invoke-RestMethod -Method Post -Uri "$BaseUrl/realms/master/protocol/openid-connect/token" -ContentType 'application/x-www-form-urlencoded' -Body $body
    return $token.access_token
}

function Invoke-Keycloak {
    param(
        [Parameter(Mandatory)][string]$Method,
        [Parameter(Mandatory)][string]$Path,
        [object]$Body = $null,
        [int[]]$OkStatus = @(200,201,204,404)
    )

    $headers = @{ Authorization = "Bearer $script:Token" }
    $uri = "$BaseUrl/admin$Path"
    $json = $null
    if ($null -ne $Body) {
        $json = $Body | ConvertTo-Json -Depth 20
    }

    try {
        return Invoke-RestMethod -Method $Method -Uri $uri -Headers $headers -ContentType 'application/json' -Body $json
    } catch {
        $status = [int]$_.Exception.Response.StatusCode
        if ($OkStatus -contains $status) {
            return $null
        }
        throw
    }
}

function Upsert-Realm {
    $existing = Invoke-Keycloak -Method Get -Path "/realms/$Realm" -OkStatus @(200,404)
    $realmBody = @{
        realm = $Realm
        enabled = $true
        registrationAllowed = $true
        resetPasswordAllowed = $true
        loginWithEmailAllowed = $true
        duplicateEmailsAllowed = $false
    }

    if ($null -eq $existing) {
        Invoke-Keycloak -Method Post -Path '/realms' -Body $realmBody -OkStatus @(201,204) | Out-Null
        Write-Step "created realm '$Realm'"
    } else {
        Invoke-Keycloak -Method Put -Path "/realms/$Realm" -Body $realmBody -OkStatus @(204) | Out-Null
        Write-Step "updated realm '$Realm'"
    }
}

function Get-Client([string]$ClientId) {
    $clients = Invoke-Keycloak -Method Get -Path "/realms/$Realm/clients?clientId=$ClientId" -OkStatus @(200)
    return @($clients) | Select-Object -First 1
}

function Upsert-Client([hashtable]$Body) {
    $existing = Get-Client $Body.clientId
    if ($null -eq $existing) {
        Invoke-Keycloak -Method Post -Path "/realms/$Realm/clients" -Body $Body -OkStatus @(201,204) | Out-Null
        Write-Step "created client '$($Body.clientId)'"
    } else {
        Invoke-Keycloak -Method Put -Path "/realms/$Realm/clients/$($existing.id)" -Body $Body -OkStatus @(204) | Out-Null
        Write-Step "updated client '$($Body.clientId)'"
    }
}

function Upsert-User([string]$Subject, [string]$Username, [string]$Email, [string]$FirstName, [string]$LastName, [string]$Password, [string]$Label = 'development user') {
    $users = Invoke-Keycloak -Method Get -Path "/realms/$Realm/users?username=$Username&exact=true" -OkStatus @(200)
    $existing = @($users) | Select-Object -First 1
    $body = @{
        id = $Subject
        username = $Username
        email = $Email
        firstName = $FirstName
        lastName = $LastName
        enabled = $true
        emailVerified = $true
        requiredActions = @()
        attributes = @{ planvexa_subject = @($Subject) }
    }

    if ($null -eq $existing) {
        Invoke-Keycloak -Method Post -Path "/realms/$Realm/users" -Body $body -OkStatus @(201,204) | Out-Null
        $users = Invoke-Keycloak -Method Get -Path "/realms/$Realm/users?username=$Username&exact=true" -OkStatus @(200)
        $existing = @($users) | Select-Object -First 1
        Write-Step "created $Label '$Username'"
    } else {
        Invoke-Keycloak -Method Put -Path "/realms/$Realm/users/$($existing.id)" -Body $body -OkStatus @(204) | Out-Null
        Write-Step "updated $Label '$Username'"
    }

    # No password supplied: leave the account credential-less rather than inventing a well-known one.
    # An operator sets it in the Keycloak admin console or re-runs with BOOTSTRAP_ADMIN_PASSWORD.
    if ([string]::IsNullOrWhiteSpace($Password)) {
        Write-Warning "[keycloak-bootstrap] no password set for '$Username'. Set BOOTSTRAP_ADMIN_PASSWORD or assign one in the Keycloak admin console before first sign-in."
        return
    }

    $credential = @{ type = 'password'; value = $Password; temporary = $false }
    Invoke-Keycloak -Method Put -Path "/realms/$Realm/users/$($existing.id)/reset-password" -Body $credential -OkStatus @(204) | Out-Null
}

Wait-Keycloak
$script:Token = Get-AdminToken
Upsert-Realm

$webClient = @{
    clientId = $WebClientId
    name = 'Planvexa Web'
    protocol = 'openid-connect'
    publicClient = $true
    standardFlowEnabled = $true
    implicitFlowEnabled = $false
    directAccessGrantsEnabled = $false
    serviceAccountsEnabled = $false
    redirectUris = @("$WebOrigin/auth/callback", "$WebOrigin/*")
    webOrigins = @($WebOrigin)
    attributes = @{
        'pkce.code.challenge.method' = 'S256'
        'post.logout.redirect.uris' = "$WebOrigin/*"
    }
    protocolMappers = @(
        @{
            name = 'audience-planvexa-api'
            protocol = 'openid-connect'
            protocolMapper = 'oidc-audience-mapper'
            consentRequired = $false
            config = @{
                'included.client.audience' = $ApiClientId
                'access.token.claim' = 'true'
                'id.token.claim' = 'false'
            }
        },
        # Populates the "amr" (Authentication Method Reference, RFC 8176) claim with the reference
        # value of every authenticator execution the user completed -- UserContextMiddleware reads this
        # to decide HasVerifiedMfa for Workspace MFA enforcement (WorkspaceResolutionMiddleware).
        # REQUIRES ONE MANUAL REALM STEP this script does not perform (Keycloak's execution "reference"
        # config shape varies enough across versions that scripting it blind risks silently breaking
        # bootstrap for every developer): in Authentication -> Browser -> Browser - Conditional OTP ->
        # OTP Form, set "Reference" to `otp`. Without that step this mapper still runs but "amr" only
        # ever contains the password factor, so MfaRequired stays enforced-but-permanently-blocking for
        # every member -- verify this step before enabling MfaRequired on any real workspace.
        @{
            name = 'amr-authentication-method-reference'
            protocol = 'openid-connect'
            protocolMapper = 'oidc-amr-mapper'
            consentRequired = $false
            config = @{
                'access.token.claim' = 'true'
                'id.token.claim' = 'false'
            }
        }
    )
}
Upsert-Client $webClient

$apiClient = @{
    clientId = $ApiClientId
    name = 'Planvexa API'
    protocol = 'openid-connect'
    publicClient = $false
    bearerOnly = $true
    standardFlowEnabled = $false
    implicitFlowEnabled = $false
    directAccessGrantsEnabled = $false
    serviceAccountsEnabled = $false
    authorizationServicesEnabled = $false
}
Upsert-Client $apiClient

$defaultPassword = if ($env:PLANVEXA_DEV_PASSWORD) { $env:PLANVEXA_DEV_PASSWORD } else { 'PlanvexaDev!123' }

# Mirrors PlanvexaBootstrap on the API side: the development seed's four accounts and the bootstrap
# admin are alternatives, not additions. dev-admin already holds admin@planvexa.local, and the realm
# forbids duplicate emails, so seeding both would fail (or silently re-key an existing account).
# An explicit BOOTSTRAP_ADMIN_EMAIL opts in regardless.
if (-not $IncludeDevelopmentUsers -or $env:BOOTSTRAP_ADMIN_EMAIL) {
    $adminPassword = $BootstrapAdminPassword
    if ([string]::IsNullOrWhiteSpace($adminPassword) -and $IncludeDevelopmentUsers) { $adminPassword = $defaultPassword }
    Upsert-User $BootstrapAdminSubject $BootstrapAdminEmail $BootstrapAdminEmail 'Planvexa' 'Admin' $adminPassword 'bootstrap admin'
}

if ($IncludeDevelopmentUsers) {
    Upsert-User 'dev-owner' 'owner@planvexa.local' 'owner@planvexa.local' 'Dev' 'Owner' ($(if ($env:PLANVEXA_DEV_OWNER_PASSWORD) { $env:PLANVEXA_DEV_OWNER_PASSWORD } else { $defaultPassword }))
    Upsert-User 'dev-admin' 'admin@planvexa.local' 'admin@planvexa.local' 'Dev' 'Admin' ($(if ($env:PLANVEXA_DEV_ADMIN_PASSWORD) { $env:PLANVEXA_DEV_ADMIN_PASSWORD } else { $defaultPassword }))
    Upsert-User 'dev-member' 'member@planvexa.local' 'member@planvexa.local' 'Dev' 'Member' ($(if ($env:PLANVEXA_DEV_MEMBER_PASSWORD) { $env:PLANVEXA_DEV_MEMBER_PASSWORD } else { $defaultPassword }))
    Upsert-User 'dev-guest' 'guest@planvexa.local' 'guest@planvexa.local' 'Dev' 'Guest' ($(if ($env:PLANVEXA_DEV_GUEST_PASSWORD) { $env:PLANVEXA_DEV_GUEST_PASSWORD } else { $defaultPassword }))
    Write-Step "realm '$Realm' bootstrap complete. Development usernames: owner@planvexa.local, admin@planvexa.local, member@planvexa.local, guest@planvexa.local"
} else {
    Write-Step "realm '$Realm' bootstrap complete. Bootstrap admin: $BootstrapAdminEmail (development users skipped)."
}
