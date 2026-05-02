# Serial Port Permission Management

## Overview

On Linux, serial devices such as `/dev/ttyUSB0` may require explicit file permissions. ComCross provides a platform abstraction for checking access, requesting temporary permission changes, and surfacing manual instructions.

This document covers permission handling only. Serial port discovery and connection behavior belong to the serial adapter plugin.

## Components

- `ISerialPortAccessManager`
  - platform abstraction
  - checks access to a specific device path
  - requests temporary access
  - returns manual permission instructions

- `LinuxSerialPortAccessManager`
  - Linux implementation
  - checks access by probing the device file
  - can request temporary access with `pkexec chmod 666 <device>`

- `DefaultSerialPortAccessManager`
  - default non-Linux implementation

- `SerialPortAccessDeniedException`
  - carries the denied device path

- `SerialPortPermissionService`
  - coordinates user-facing permission requests and notifications

## Connection Failure Flow

```text
User connects a serial session
  -> serial adapter attempts to open the selected port
  -> platform access check fails
  -> SerialPortAccessDeniedException is raised
  -> Core/Shell connection flow surfaces notification/dialog guidance
```

## Temporary Permission Request

When the user chooses a temporary permission fix:

```text
SerialPortPermissionService.RequestPermissionAsync(path)
  -> LinuxSerialPortAccessManager invokes pkexec chmod 666 <path>
  -> success: notify temporary access granted
  -> failure: notify failure and show manual instructions
```

The temporary permission is not persistent. It may disappear after device reconnect or reboot.

## Manual Instructions

Temporary access for testing:

```bash
sudo chmod 666 /dev/ttyUSB0
```

Persistent group-based access:

```bash
sudo usermod -aG dialout $USER
```

The user must log out and back in after changing group membership.

udev rule example:

```bash
# /etc/udev/rules.d/50-serial.rules
KERNEL=="ttyUSB[0-9]*", MODE="0666"
KERNEL=="ttyACM[0-9]*", MODE="0666"

sudo udevadm control --reload-rules
```

## Security Notes

ComCross prefers temporary per-device permission changes over silently changing persistent user groups.

Runtime permission enforcement is not a complete security model. Release security hardening, including installer guidance and plugin trust policy, is tracked for post-v0.4 work.
