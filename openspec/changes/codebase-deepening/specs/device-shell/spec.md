# Spec Delta

## ADDED Requirements

### Requirement: Linux device and transfer lists bind collections
The Linux Devices, Transfers, and Connection views SHALL render lists and detail panes from ViewModel collections through AXAML templates. Those views MUST NOT rebuild the list by clearing visual children and constructing tiles in code-behind. Views MUST receive the local-channel client from composition, and MUST NOT construct it themselves.

#### Scenario: Trusted device appears
- **WHEN** the local shell state gains a trusted device
- **THEN** the Devices view shows that device through the bound collection without a code-behind rebuild of the group panel
