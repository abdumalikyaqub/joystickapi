using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using SharpDX.DirectInput;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace JoystickAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
public class JoystickController : ControllerBase
{
    private static Joystick _joystick;
    private static readonly IPEndPoint _remoteEP = new IPEndPoint(IPAddress.Parse("172.20.76.119"), 5000);
    private static readonly UdpClient _udpClient = new UdpClient();

    static JoystickController() => InitializeJoystick();

    [HttpGet("status")]
    public Task<IActionResult> GetJoystickStatus()
    {
        if (_joystick == null)
            return Task.FromResult<IActionResult>(NotFound("Джойстики не найдены"));

        var (steer, speed, gear) = ProcessJoystickInput(_joystick);
        return Task.FromResult<IActionResult>(Ok(new { steer, speed, gear }));
    }

    [HttpPost("send")]
    public async Task<IActionResult> SendJoystickData()
    {
        if (_joystick == null)
            return NotFound("Джойстики не найдены");

        var (steer, speed, gear) = ProcessJoystickInput(_joystick);
        await SendUdpData(steer, speed);

        return Ok(new { steer, speed, gear });
    }

    private static void InitializeJoystick()
    {
        var directInput = new DirectInput();
        var joysticks = directInput.GetDevices(DeviceType.Driving, DeviceEnumerationFlags.AttachedOnly);

        if (joysticks.Count > 0)
        {
            _joystick = new Joystick(directInput, joysticks[0].InstanceGuid);
            _joystick.Properties.BufferSize = 128;
            _joystick.Acquire();
        }
    }

    private static (int steer, int speed, int gear) ProcessJoystickInput(Joystick joystick)
    {
        const int maxSpeed = 1000;
        const int maxSteer = 200;

        joystick.Poll();
        var joystickState = joystick.GetCurrentState();

        int gear = GetGearStatus(joystickState);
        int steer = CalculateSteering(joystickState, maxSteer);
        int speed = CalculateSpeed(joystickState, maxSpeed, gear);

        return (steer, speed, gear);
    }

    private static int GetGearStatus(JoystickState joystickState)
    {
        if (joystickState.Buttons[12]) return 1;
        if (joystickState.Buttons[13]) return 2;
        if (joystickState.Buttons[14]) return 3;
        if (joystickState.Buttons[15]) return 4;
        if (joystickState.Buttons[16]) return 5;
        if (joystickState.Buttons[17]) return 6;
        if (joystickState.Buttons[11]) return 7;
        return 0;
    }

    private static int CalculateSteering(JoystickState joystickState, int maxSteer)
    {
        return (int)((joystickState.X * maxSteer / 65535.0f) - maxSteer / 2);
    }

    private static int CalculateSpeed(JoystickState joystickState, int maxSpeed, int gear)
    {
        int speed = (int)(((-joystickState.Y + 65535.0f) / 2) / 65535.0f * maxSpeed / 6 * gear);

        if (gear == 1 && speed > 80) speed = 80;
        if (gear > 1 && speed > 100) speed = 100;
        if (gear == 7) speed = -speed / 5;

        return speed;
    }

    private static async Task SendUdpData(int steer, int speed)
    {
        try
        {
            string message = JsonConvert.SerializeObject(new { steer, speed });
            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            await _udpClient.SendAsync(messageBytes, messageBytes.Length, _remoteEP);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Ошибка отправки данных: " + ex.Message);
        }
    }
}
