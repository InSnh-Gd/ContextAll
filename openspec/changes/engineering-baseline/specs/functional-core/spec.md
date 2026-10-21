# Spec Delta

## Purpose

Lets the protocol core decide the next state without performing I/O, and lets the host carry out the resulting effects through delegates.

## ADDED Requirements

### Requirement: Decisions are pure
A core decision SHALL take the current state and an input and return either a refusal or the next state together with the effects to run. The decision MUST NOT send bytes, write files, or read the clock. The same state and input MUST produce the same result.

#### Scenario: Decision is repeated
- **WHEN** the same state and the same input are decided twice
- **THEN** both results are equal, and no bytes were sent and no file was written by the decision

### Requirement: Expected refusal is a value
When a decision refuses an input for an expected reason, it SHALL return a failure value and MUST NOT throw for that reason. The previously accepted state MUST remain the current state.

#### Scenario: Input is refused
- **WHEN** a decision refuses an input
- **THEN** the caller receives a failure value, no exception is thrown for that refusal, and the accepted state is unchanged

### Requirement: Effects run through a delegate
The host SHALL perform each effect returned by a decision by invoking an `Action` or `Func` registered for that effect. The decision itself MUST NOT invoke that delegate.

#### Scenario: Decision returns an effect
- **WHEN** a decision returns an effect
- **THEN** the host invokes the delegate registered for that effect, and the decision did not invoke it

### Requirement: Waits use await
Production code that needs the result of an incomplete asynchronous operation SHALL await it. It MUST NOT block the caller to obtain that result.

#### Scenario: Result is not ready
- **WHEN** production code needs a result that is not ready yet
- **THEN** it awaits the operation and does not block the caller until the result is stored

### Requirement: Null-forgiving operator stays reviewed
Production code MUST NOT include the null-forgiving operator unless the operator has approved that use.

#### Scenario: Approval is missing
- **WHEN** production code would use the null-forgiving operator and the operator has not approved that use
- **THEN** the change does not include that operator
