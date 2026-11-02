function Clone-Repository {
    param (
        [string]$repoUrl,
        [string]$localRepoPath,
        [string]$branchName = "main"
    )
	
	# Use the main branch if no branch is specified
    if (-not $branchName) {
        $branchName = "main"
    }

    Write-Host "Cloning $repoUrl to $localRepoPath on branch $branchName..."
    git clone --branch $branchName $repoUrl $localRepoPath
}

function Build-Solution {
    param (
        [string]$localRepoPath
    )

    # Navigate to the local repository path
    Set-Location -Path $localRepoPath

    # Search for solution file (.sln)
    $solutionFile = Get-ChildItem -Filter *.sln -Recurse | Select-Object -First 1

    if ($null -ne $solutionFile) {
        Write-Host "Building solution $($solutionFile.FullName)..."
        dotnet build $solutionFile.FullName --configuration Release
        Write-Host "Build completed!"

        return $true
    } else {
        Write-Host "No solution file found!"
        return $false
    }
}

function Copy-BuildOutputs {
    param (
        [string]$localRepoPath,
        [string]$outputDirectory
    )

    # Search for DLL and XML files
    $filesToCopy = Get-ChildItem -Path $localRepoPath -Recurse |
        Where-Object { 
            ($_.DirectoryName -like "*\bin\Release*") -and
            ($_.Extension -eq ".dll" -or $_.Extension -eq ".xml")
        }

    # Copy each file to the output directory
    foreach ($file in $filesToCopy) {
        $destinationPath = Join-Path -Path $outputDirectory -ChildPath $file.Name
        Copy-Item -Path $file.FullName -Destination $destinationPath -Force
    }

    Write-Host "Copied build output files to $outputDirectory."	
}

function Delete-Repository {
    param (
        [string]$localRepoPath
    )

    Write-Host "Deleting repository at $localRepoPath..."
    Remove-Item -Path $localRepoPath -Recurse -Force
    Write-Host "Repository deleted."
}

# Define base URL and repository details with target branches
$baseUrl = "https://github.com/Nanoservice-Team/"
# Read XML configuration
[xml]$xmlData = Get-Content -Path "repos.xml"
$repos = $xmlData.Repositories.Repository

# Get the directory where the script is located
$scriptDirectory = (Get-Location).Path
$outputDirectory = Join-Path -Path (Join-Path -Path $scriptDirectory -ChildPath ".") -ChildPath "NsStore"

if (-Not (Test-Path -Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory
}

# Loop through each repository and process sequentially
foreach ($repo in $repos) {
    $repoUrl = "$baseUrl$($repo.Name).git"
    $branchName = $repo.Branch
    $localRepoPath = Join-Path -Path $scriptDirectory -ChildPath $repo.Name

    Clone-Repository -repoUrl $repoUrl -localRepoPath $localRepoPath -branchName $branchName
    if (Build-Solution -localRepoPath $localRepoPath) {
        Copy-BuildOutputs -localRepoPath $localRepoPath -outputDirectory $outputDirectory
		
		# Change the current directory to the script directory
        Set-Location -Path $scriptDirectory

        # Now, delete the repository
		Delete-Repository -localRepoPath $localRepoPath
    }
}

Set-Location -Path $scriptDirectory
Write-Host "All operations completed!"