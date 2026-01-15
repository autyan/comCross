# Serial Port Permission Management

## 概述

在 Linux 系统下，访问串口设备（如 `/dev/ttyUSB0`）需要特定的权限。本系统提供了一套完整的权限管理机制，包括：

1. **精确的权限检测**：检查对具体串口设备文件的实际访问权限
2. **自动提权请求**：使用 `pkexec` 临时修改设备权限（安全，非持久化）
3. **用户通知**：权限不足时自动发送通知，提供手动操作指引

## 架构设计

### 接口抽象

```
ISerialPortAccessManager (接口)
    ├─ LinuxSerialPortAccessManager (Linux 实现)
    └─ DefaultSerialPortAccessManager (Windows/macOS 实现)
```

### 核心组件

1. **ISerialPortAccessManager**: 平台抽象接口
   - `HasAccessPermissionAsync()`: 检查权限
   - `RequestAccessPermissionAsync()`: 请求提权
   - `GetManualPermissionInstructions()`: 获取手动操作指南

2. **LinuxSerialPortAccessManager**: Linux 平台实现
   - 检查对具体设备文件的访问权限（通过尝试打开文件）
   - 使用 `pkexec chmod 666 /dev/ttyXXX` 临时修改设备权限
   - 提供多种手动操作方案

3. **SerialPortAccessDeniedException**: 权限异常
   - 在权限不足时抛出，携带端口路径信息

4. **SerialPortPermissionService**: 权限请求服务
   - 处理权限请求流程
   - 发送通知给用户

## 工作流程

### 连接串口时

```
用户请求连接串口
    ↓
SerialConnection.OpenAsync()
    ↓
检查权限 (HasAccessPermissionAsync)
    ↓
    ├─ 有权限 → 正常打开串口
    └─ 无权限 → 抛出 SerialPortAccessDeniedException
                    ↓
            DeviceService 捕获异常
                    ↓
            发送通知 (NotificationService)
```

### 权限请求流程

用户可以通过 UI 或通知触发权限请求：

```
用户点击请求权限
    ↓
SerialPortPermissionService.RequestPermissionAsync()
    ↓
尝试 pkexec chmod 666 /dev/ttyUSB0
    ↓
    ├─ 成功 → 发送"已授权"通知（临时）
    └─ 失败 → 发送"授权失败"通知 + 手动指引
```

## 安全性考虑

### ✅ 采用的安全方案（临时提权）

- **操作**: `pkexec chmod 666 /dev/ttyUSB0`
- **影响范围**: 仅影响单个设备文件
- **持久性**: 临时，设备重新连接或重启后失效
- **风险**: 低，仅在本次使用期间有效

### ❌ 避免的高危方案（永久修改）

- **操作**: `usermod -aG dialout username`
- **影响范围**: 修改用户系统权限
- **持久性**: 永久，需要注销重新登录
- **风险**: 高，属于系统级持久化修改

## 手动操作指引

系统提供三种手动操作方案：

### 方案 1: 临时访问（推荐用于测试）
```bash
sudo chmod 666 /dev/ttyUSB0
```
- 立即生效，设备重连后失效

### 方案 2: 永久访问（推荐用于日常使用）
```bash
sudo usermod -aG dialout $USER
# 需要注销并重新登录
```

### 方案 3: udev 规则（推荐用于系统管理员）
```bash
# 创建 /etc/udev/rules.d/50-serial.rules
KERNEL=="ttyUSB[0-9]*", MODE="0666"
KERNEL=="ttyACM[0-9]*", MODE="0666"

# 重新加载规则
sudo udevadm control --reload-rules
```

## 国际化支持

已添加的本地化字符串：

- `notification.permission.denied`: 权限被拒绝通知
- `notification.permission.clickToFix`: 点击修复提示
- `notification.permission.temporaryGranted`: 临时授权成功
- `notification.permission.failed`: 授权失败

## 使用示例

### 在 Shell 中集成

```csharp
// MainWindowViewModel.cs
public class MainWindowViewModel
{
    private readonly SerialPortPermissionService _permissionService;
    
    public MainWindowViewModel()
    {
        // 创建权限管理器（自动根据平台选择）
        var adapter = new SerialAdapter();
        
        // 创建权限服务
        _permissionService = new SerialPortPermissionService(
            adapter.AccessManager, 
            _notificationService);
    }
    
    // 处理连接失败时的权限请求
    private async Task OnConnectFailed(string port)
    {
        var success = await _permissionService.RequestPermissionAsync(port);
        if (success)
        {
            // 重试连接
            await RetryConnect(port);
        }
        else
        {
            // 显示手动操作指引
            var instructions = _permissionService.GetManualInstructions(port);
            ShowInstructionsDialog(instructions);
        }
    }
}
```

## 测试建议

### Linux 环境测试

1. 移除用户的 dialout 组权限：
   ```bash
   sudo gpasswd -d $USER dialout
   ```

2. 重新登录后测试：
   - 连接串口应触发权限检查
   - 应收到权限不足通知
   - 点击通知可触发 pkexec 提权对话框

3. 测试手动授权：
   ```bash
   sudo chmod 666 /dev/ttyUSB0
   ```

### Windows 环境测试

- 应该不需要特殊权限
- DefaultSerialPortAccessManager 应返回始终有权限

## 未来改进

1. **GUI 对话框**：在通知中添加"请求权限"按钮
2. **权限状态缓存**：避免重复检查
3. **更多平台支持**：macOS 特定处理
4. **权限恢复通知**：设备重连时提醒权限已重置
