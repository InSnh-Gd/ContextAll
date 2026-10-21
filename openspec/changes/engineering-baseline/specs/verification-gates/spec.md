# Spec Delta

## Purpose

Proves a change with the same test suite before a push leaves the machine and again after the push reaches GitHub.

## ADDED Requirements

### Requirement: Tests come first
A production behavior SHALL be introduced together with a test that fails when that behavior is absent. The test MUST be part of the same change as the behavior.

#### Scenario: Behavior is added
- **WHEN** a change adds a production behavior
- **THEN** the same change includes a test that fails without that behavior and passes with it

### Requirement: Push runs the suite on GitHub
A push to the GitHub repository SHALL start a GitHub Actions workflow that runs the automated test suite. A failed suite MUST mark that workflow run as failed.

#### Scenario: Tests pass on the remote
- **WHEN** a push reaches GitHub and the test suite passes
- **THEN** the workflow run succeeds

#### Scenario: Tests fail on the remote
- **WHEN** a push reaches GitHub and the test suite fails
- **THEN** the workflow run fails

### Requirement: Pre-push hook runs the same suite
The repository SHALL provide a git pre-push hook that runs the same automated test suite as the GitHub Actions workflow. When that suite fails, the hook MUST stop the push.

#### Scenario: Local suite passes
- **WHEN** the operator pushes and the pre-push suite passes
- **THEN** the hook allows the push to proceed

#### Scenario: Local suite fails
- **WHEN** the operator pushes and the pre-push suite fails
- **THEN** the hook stops the push and the commits are not sent
