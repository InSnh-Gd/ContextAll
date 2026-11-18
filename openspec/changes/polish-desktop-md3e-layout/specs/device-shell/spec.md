# Spec Delta

## ADDED Requirements

### Requirement: Desktop first-device guidance
The desktop shell SHALL offer first-connection guidance when the Daemon is ready for discovery and both trusted and discovered lists are empty. The list SHALL retain one global clipboard broadcast entry without requiring a selected device.

#### Scenario: No devices are available
- **WHEN** discovery is ready and both lists are empty
- **THEN** the wide layout shows discovery on the left and same-LAN, second-app and pairing-code guidance with a link to manual connection on the right; the narrow layout exposes the guidance and link from the list without a second pane.

#### Scenario: A device appears or the Daemon disconnects
- **WHEN** a device is present but none is selected
- **THEN** the first-connection guide yields to the selection prompt in the wide detail pane, and selecting a device shows its real details.
- **AND** a disconnected Daemon is represented by the service status, not by an active search indicator.

### Requirement: Desktop connection workspace
The desktop shell SHALL arrange manual dialing and local network information using the shell window width and SHALL retain visible field labels and a full rounded outline for address and alias in both themes.

#### Scenario: Resize with a draft
- **WHEN** the window crosses 840dp with a partially entered address or alias
- **THEN** at or above 840dp the panes appear side by side with 28dp corners and a 12dp gap; below 840dp dialing precedes network information in a vertical flow, with both values preserved.

### Requirement: Responsive grouped desktop settings
The desktop shell SHALL expose the same five settings categories at both widths. Rows SHALL use 16sp names, 14sp supporting text, at least 72dp height, 2dp gaps, 24dp outer and 6dp adjoining corners, without row outlines or dividers.

#### Scenario: Wide category editor
- **WHEN** the window is at least 840dp wide on This Device
- **THEN** category navigation remains visible and choosing a category shows only its settings in the editor, with all existing operations reachable.

#### Scenario: Narrow settings list
- **WHEN** the window is narrower than 840dp
- **THEN** identity, appearance, files and background, window, and help appear in that order in one scrollable column up to 640dp wide. Long descriptions wrap and grow the row; resizing does not discard in-progress edits.

### Requirement: Desktop transfer totals accompany history
The desktop shell SHALL place the existing sent, received and completed-segments/transfers totals after the transfer history, below the immediate actions and queue rather than above the workspace.

#### Scenario: History is populated or empty
- **WHEN** the user opens Transfers at either width
- **THEN** exactly one summary uses the current aggregate values after the history section, including when no transfer records are available.

### Requirement: Actual desktop title bar feedback
The desktop shell SHALL distinguish the stored system title bar preference from the effective window decoration. Explanatory text SHALL follow the current window state without persisting that state as a preference.

#### Scenario: Preference and effective decoration differ
- **WHEN** system decorations are preferred but negotiation finishes with an application-drawn title bar
- **THEN** the switch remains on and the description states that this desktop did not provide a system title bar and the application title bar is in use.

#### Scenario: Preference toggled or negotiation updated
- **WHEN** the preference is off, or the shell's existing decoration negotiation settles on system decorations, or negotiation is still pending
- **THEN** the description identifies the application title bar, identifies the system title bar, or withholds a settled outcome respectively; it updates without reopening settings and only one title bar is shown after settlement.
