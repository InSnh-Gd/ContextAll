# Spec Delta

## ADDED Requirements

### Requirement: One local-command module
Validation, request-id uniqueness, and dispatch of local operator commands SHALL live in one module. The Linux local channel adapter and the Android JNI adapter MUST call that module. They MUST NOT each copy request-id checks or command matrices.

#### Scenario: Duplicate request id on either adapter
- **WHEN** a local command with a repeated request id arrives on the Unix socket, the Windows pipe, or the Android JNI command JSON
- **THEN** that adapter reports the same duplicate-request failure produced by the shared module
