using System;
using System.Linq;
using NUnit.Framework;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Experimental.Input;
using UnityEngine.Experimental.Input.Controls;
using UnityEngine.Experimental.Input.LowLevel;
using UnityEngine.Experimental.Input.Utilities;

partial class CoreTests
{
    // This is one of the most central tests. If this one breaks, it most often
    // hints at the state layouting or state updating machinery being borked.
    [Test]
    [Category("Events")]
    public void Events_CanUpdateStateOfDeviceWithEvent()
    {
        var gamepad = (Gamepad)InputSystem.AddDevice("Gamepad");
        var newState = new GamepadState {leftStick = new Vector2(0.123f, 0.456f)};

        InputSystem.QueueStateEvent(gamepad, newState);
        InputSystem.Update();

        Assert.That(gamepad.leftStick.x.ReadValue(), Is.EqualTo(0.123f));
        Assert.That(gamepad.leftStick.y.ReadValue(), Is.EqualTo(0.456f));
    }

    [Test]
    [Category("Events")]
    public void Events_CanUpdatePartialStateOfDeviceWithEvent()
    {
        var gamepad = (Gamepad)InputSystem.AddDevice("Gamepad");

        // Full state update to make sure we won't be overwriting other
        // controls with state. Also, make sure we actually carry over
        // those values on buffer flips.
        InputSystem.QueueStateEvent(gamepad,
            new GamepadState
        {
            buttons = 0xffffffff,
            rightStick = Vector2.one,
            leftTrigger = 0.123f,
            rightTrigger = 0.456f
        });
        InputSystem.Update();

        // Update just left stick.
        InputSystem.QueueDeltaStateEvent(gamepad.leftStick, new Vector2(0.5f, 0.5f));
        InputSystem.Update();

        Assert.That(gamepad.leftStick.x.ReadValue(), Is.EqualTo(0.5).Within(0.000001));
        Assert.That(gamepad.leftStick.y.ReadValue(), Is.EqualTo(0.5).Within(0.000001));
        Assert.That(gamepad.leftTrigger.ReadValue(), Is.EqualTo(0.123).Within(0.000001));
        Assert.That(gamepad.rightStick.x.ReadValue(), Is.EqualTo(1).Within(0.000001));
    }

    [Test]
    [Category("Events")]
    public unsafe void Events_CanInitializeStateEventFromDevice()
    {
        var mouse = InputSystem.AddDevice<Mouse>();

        InputSystem.QueueStateEvent(mouse, new MouseState {delta = Vector2.one});
        InputSystem.Update();

        InputEventPtr eventPtr;
        using (var buffer = StateEvent.From(mouse, out eventPtr))
        {
            Assert.That(mouse.delta.x.ReadValueFrom(eventPtr), Is.EqualTo(1).Within(0.00001));
            Assert.That(mouse.delta.y.ReadValueFrom(eventPtr), Is.EqualTo(1).Within(0.00001));

            var stateEventPtr = StateEvent.From(eventPtr);
            Assert.That(stateEventPtr->baseEvent.deviceId, Is.EqualTo(mouse.id));
            Assert.That(stateEventPtr->baseEvent.time, Is.EqualTo(testRuntime.currentTime));
            Assert.That(stateEventPtr->baseEvent.sizeInBytes, Is.EqualTo(buffer.Length));
            Assert.That(stateEventPtr->baseEvent.sizeInBytes,
                Is.EqualTo(InputEvent.kBaseEventSize + sizeof(FourCC) + mouse.stateBlock.alignedSizeInBytes));
            Assert.That(stateEventPtr->stateSizeInBytes, Is.EqualTo(mouse.stateBlock.alignedSizeInBytes));
            Assert.That(stateEventPtr->stateFormat, Is.EqualTo(mouse.stateBlock.format));
        }
    }

    [Test]
    [Category("Events")]
    public void Events_SendingStateEventToDevice_MakesItCurrent()
    {
        var gamepad = InputSystem.AddDevice("Gamepad");

        // Adding a device makes it current so add another one so that .current
        // is not already set to the gamepad we just created.
        InputSystem.AddDevice("Gamepad");

        InputSystem.QueueStateEvent(gamepad, new GamepadState());
        InputSystem.Update();

        Assert.That(Gamepad.current, Is.SameAs(gamepad));
    }

    [Test]
    [Category("Events")]
    public void TODO_Events_SendingStateEvent_WithOnlyNoise_DoesNotMakeDeviceCurrent()
    {
        Assert.Fail();
    }

    [Test]
    [Category("Events")]
    public void Events_SendingStateToDeviceWithoutBeforeRenderEnabled_DoesNothingInBeforeRenderUpdate()
    {
        var gamepad = (Gamepad)InputSystem.AddDevice("Gamepad");
        var newState = new GamepadState {leftStick = new Vector2(0.123f, 0.456f)};

        InputSystem.QueueStateEvent(gamepad, newState);
        InputSystem.Update(InputUpdateType.BeforeRender);

        Assert.That(gamepad.leftStick.ReadValue(), Is.EqualTo(default(Vector2)));
    }

    [Test]
    [Category("Events")]
    public void Events_SendingStateToDeviceWithBeforeRenderEnabled_UpdatesDeviceInBeforeRender()
    {
        const string deviceJson = @"
            {
                ""name"" : ""CustomGamepad"",
                ""extend"" : ""Gamepad"",
                ""beforeRender"" : ""Update""
            }
        ";

        InputSystem.RegisterControlLayout(deviceJson);

        var gamepad = (Gamepad)InputSystem.AddDevice("CustomGamepad");
        var newState = new GamepadState {leftTrigger = 0.123f};

        InputSystem.QueueStateEvent(gamepad, newState);
        InputSystem.Update(InputUpdateType.BeforeRender);

        Assert.That(gamepad.leftTrigger.ReadValue(), Is.EqualTo(0.123f).Within(0.000001));
    }

    [Test]
    [Category("Events")]
    public void Events_CanListenToEventStream()
    {
        var device = InputSystem.AddDevice("Gamepad");

        var receivedCalls = 0;
        InputSystem.onEvent += inputEvent =>
            {
                ++receivedCalls;
                Assert.That(inputEvent.IsA<StateEvent>(), Is.True);
                Assert.That(inputEvent.deviceId, Is.EqualTo(device.id));
            };

        InputSystem.QueueStateEvent(device, new GamepadState());
        InputSystem.Update();

        Assert.That(receivedCalls, Is.EqualTo(1));
    }

    // Should be possible to have a pointer to a state event and from it, return
    // the list of controls that have non-default values.
    // Probably makes sense to also be able to return from it a list of changed
    // controls by comparing it to a device's current state.
    [Test]
    [Category("Events")]
    public void TODO_Events_CanFindActiveControlsFromStateEvent()
    {
        Assert.Fail();
    }

    [Test]
    [Category("Events")]
    public void Events_AreProcessedInOrderTheyAreQueuedIn()
    {
        const double kFirstTime = 0.5;
        const double kSecondTime = 1.5;
        const double kThirdTime = 2.5;

        var receivedCalls = 0;
        var receivedFirstTime = 0.0;
        var receivedSecondTime = 0.0;
        var receivedThirdTime = 0.0;

        InputSystem.onEvent +=
            inputEvent =>
            {
                ++receivedCalls;
                if (receivedCalls == 1)
                    receivedFirstTime = inputEvent.time;
                else if (receivedCalls == 2)
                    receivedSecondTime = inputEvent.time;
                else
                    receivedThirdTime = inputEvent.time;
            };

        var device = InputSystem.AddDevice("Gamepad");

        InputSystem.QueueStateEvent(device, new GamepadState(), kSecondTime);
        InputSystem.QueueStateEvent(device, new GamepadState(), kFirstTime);
        InputSystem.QueueStateEvent(device, new GamepadState(), kThirdTime);

        InputSystem.Update();

        Assert.That(receivedCalls, Is.EqualTo(3));
        Assert.That(receivedFirstTime, Is.EqualTo(kSecondTime).Within(0.00001));
        Assert.That(receivedSecondTime, Is.EqualTo(kFirstTime).Within(0.00001));
        Assert.That(receivedThirdTime, Is.EqualTo(kThirdTime).Within(0.00001));
    }

    [Test]
    [Category("Events")]
    public void Events_CanQueueAndReceiveEventsAgainstNonExistingDevices()
    {
        // Device IDs are looked up only *after* the system shows the event to us.

        var receivedCalls = 0;
        var receivedDeviceId = InputDevice.kInvalidDeviceId;
        InputSystem.onEvent +=
            eventPtr =>
            {
                ++receivedCalls;
                receivedDeviceId = eventPtr.deviceId;
            };

        var inputEvent = DeviceConfigurationEvent.Create(4, 1.0);
        InputSystem.QueueEvent(ref inputEvent);

        InputSystem.Update();

        Assert.That(receivedCalls, Is.EqualTo(1));
        Assert.That(receivedDeviceId, Is.EqualTo(4));
    }

    [Test]
    [Category("Events")]
    public void Events_HandledFlagIsResetWhenEventIsQueued()
    {
        var receivedCalls = 0;
        var wasHandled = true;

        InputSystem.onEvent +=
            eventPtr =>
            {
                ++receivedCalls;
                wasHandled = eventPtr.handled;
            };

        var inputEvent = DeviceConfigurationEvent.Create(4, 1.0);

        // This should go back to false when we inputEvent goes on the queue.
        // The way the behavior is implemented is a side-effect of how we store
        // the handled flag as a bit on the event ID -- which will get set by
        // native on an event when it is queued.
        inputEvent.baseEvent.handled = true;

        InputSystem.QueueEvent(ref inputEvent);

        InputSystem.Update();

        Assert.That(receivedCalls, Is.EqualTo(1));
        Assert.That(wasHandled, Is.False);
    }

    [Test]
    [Category("Events")]
    public void Events_AlreadyHandledEventsAreIgnoredWhenProcessingEvents()
    {
        // Need a device with before render enabled so we can produce
        // the effect of having already handled events in the event queue.
        // If we use an invalid device, before render updates will simply
        // ignore the event.
        const string json = @"
            {
                ""name"" : ""CustomGamepad"",
                ""extend"" : ""Gamepad"",
                ""beforeRender"" : ""Update""
            }
        ";

        InputSystem.RegisterControlLayout(json);
        var device = InputSystem.AddDevice("CustomGamepad");

        InputSystem.onEvent +=
            inputEvent => { inputEvent.handled = true; };

        var event1 = DeviceConfigurationEvent.Create(device.id, 1.0);
        var event2 = DeviceConfigurationEvent.Create(device.id, 2.0);

        InputSystem.QueueEvent(ref event1);

        // Before render update won't clear queue so after the update
        // event1 is still in there.
        InputSystem.Update(InputUpdateType.BeforeRender);

        // Add new unhandled event.
        InputSystem.QueueEvent(ref event2);

        var receivedCalls = 0;
        var receivedTime = 0.0;

        InputSystem.onEvent +=
            inputEvent =>
            {
                ++receivedCalls;
                receivedTime = inputEvent.time;
            };
        InputSystem.Update();

        // On the second update, we should have seen only event2.
        Assert.That(receivedCalls, Is.EqualTo(1));
        Assert.That(receivedTime, Is.EqualTo(2.0).Within(0.00001));
    }

    [Test]
    [Category("Events")]
    public void Events_CanPreventEventsFromBeingProcessed()
    {
        InputSystem.onEvent +=
            inputEvent =>
            {
                // If we mark the event handled, the system should skip it and not
                // let it go to the device.
                inputEvent.handled = true;
            };

        var device = (Gamepad)InputSystem.AddDevice("Gamepad");

        InputSystem.QueueStateEvent(device, new GamepadState {rightTrigger = 0.45f});
        InputSystem.Update();

        Assert.That(device.rightTrigger.ReadValue(), Is.EqualTo(0.0).Within(0.00001));
    }

    [Test]
    [Category("Events")]
    public unsafe void Events_CanTraceEventsOfDevice()
    {
        var device = InputSystem.AddDevice("Gamepad");
        var noise = InputSystem.AddDevice("Gamepad");

        using (var trace = new InputEventTrace {deviceId = device.id})
        {
            trace.Enable();
            Assert.That(trace.enabled, Is.True);

            var firstState = new GamepadState {rightTrigger = 0.35f};
            var secondState = new GamepadState {leftTrigger = 0.75f};

            InputSystem.QueueStateEvent(device, firstState, 0.5);
            InputSystem.QueueStateEvent(device, secondState, 1.5);
            InputSystem.QueueStateEvent(noise, new GamepadState()); // This one just to make sure we don't get it.

            InputSystem.Update();

            trace.Disable();

            var events = trace.ToList();

            Assert.That(events, Has.Count.EqualTo(2));

            Assert.That(events[0].type, Is.EqualTo((FourCC)StateEvent.Type));
            Assert.That(events[0].deviceId, Is.EqualTo(device.id));
            Assert.That(events[0].time, Is.EqualTo(0.5).Within(0.000001));
            Assert.That(events[0].sizeInBytes, Is.EqualTo(StateEvent.GetEventSizeWithPayload<GamepadState>()));
            Assert.That(UnsafeUtility.MemCmp(UnsafeUtility.AddressOf(ref firstState),
                    StateEvent.From(events[0])->state.ToPointer(), UnsafeUtility.SizeOf<GamepadState>()), Is.Zero);

            Assert.That(events[1].type, Is.EqualTo((FourCC)StateEvent.Type));
            Assert.That(events[1].deviceId, Is.EqualTo(device.id));
            Assert.That(events[1].time, Is.EqualTo(1.5).Within(0.000001));
            Assert.That(events[1].sizeInBytes, Is.EqualTo(StateEvent.GetEventSizeWithPayload<GamepadState>()));
            Assert.That(UnsafeUtility.MemCmp(UnsafeUtility.AddressOf(ref secondState),
                    StateEvent.From(events[1])->state.ToPointer(), UnsafeUtility.SizeOf<GamepadState>()), Is.Zero);
        }
    }

    [Test]
    [Category("Events")]
    public void Events_WhenTraceIsFull_WillStartOverwritingOldEvents()
    {
        var device = InputSystem.AddDevice("Gamepad");
        using (var trace =
                   new InputEventTrace(StateEvent.GetEventSizeWithPayload<GamepadState>() * 2) {deviceId = device.id})
        {
            trace.Enable();

            var firstState = new GamepadState {rightTrigger = 0.35f};
            var secondState = new GamepadState {leftTrigger = 0.75f};
            var thirdState = new GamepadState {leftTrigger = 0.95f};

            InputSystem.QueueStateEvent(device, firstState, 0.5);
            InputSystem.QueueStateEvent(device, secondState, 1.5);
            InputSystem.QueueStateEvent(device, thirdState, 2.5);

            InputSystem.Update();

            trace.Disable();

            var events = trace.ToList();

            Assert.That(events, Has.Count.EqualTo(2));
            Assert.That(events, Has.Exactly(1).With.Property("time").EqualTo(1.5).Within(0.000001));
            Assert.That(events, Has.Exactly(1).With.Property("time").EqualTo(2.5).Within(0.000001));
        }
    }

    [Test]
    [Category("Events")]
    public void Events_CanClearEventTrace()
    {
        using (var trace = new InputEventTrace())
        {
            trace.Enable();

            var device = InputSystem.AddDevice("Gamepad");
            InputSystem.QueueStateEvent(device, new GamepadState());
            InputSystem.QueueStateEvent(device, new GamepadState());
            InputSystem.Update();

            Assert.That(trace.ToList(), Has.Count.EqualTo(2));

            trace.Clear();

            Assert.That(trace.ToList(), Has.Count.EqualTo(0));
        }
    }

    [Test]
    [Category("Events")]
    public void Events_GetUniqueIds()
    {
        var device = InputSystem.AddDevice("Gamepad");

        InputSystem.QueueStateEvent(device, new GamepadState());
        InputSystem.QueueStateEvent(device, new GamepadState());

        var receivedCalls = 0;
        var firstId = InputEvent.kInvalidId;
        var secondId = InputEvent.kInvalidId;

        InputSystem.onEvent +=
            eventPtr =>
            {
                ++receivedCalls;
                if (receivedCalls == 1)
                    firstId = eventPtr.id;
                else if (receivedCalls == 2)
                    secondId = eventPtr.id;
            };

        InputSystem.Update();

        Assert.That(firstId, Is.Not.EqualTo(secondId));
    }

    [Test]
    [Category("Events")]
    public void Events_DoNotLeakIntoNextUpdate()
    {
        var device = InputSystem.AddDevice("Gamepad");

        InputSystem.QueueStateEvent(device, new GamepadState(), 1.0);
        InputSystem.QueueStateEvent(device, new GamepadState(), 2.0);

        var receivedUpdateCalls = 0;
        var receivedEventCount = 0;

        Action<InputUpdateType, int, IntPtr> onUpdate =
            (updateType, eventCount, eventData) =>
            {
                ++receivedUpdateCalls;
                receivedEventCount += eventCount;
            };
        testRuntime.onUpdate += onUpdate;

        InputSystem.Update();

        Assert.That(receivedUpdateCalls, Is.EqualTo(1));
        Assert.That(receivedEventCount, Is.EqualTo(2));

        receivedEventCount = 0;
        receivedUpdateCalls = 0;

        InputSystem.Update();

        Assert.That(receivedEventCount, Is.Zero);
        Assert.That(receivedUpdateCalls, Is.EqualTo(1));
    }

    [Test]
    [Category("Events")]
    public void Events_IfOldStateEventIsSentToDevice_IsIgnored()
    {
        var gamepad = (Gamepad)InputSystem.AddDevice("Gamepad");

        InputSystem.QueueStateEvent(gamepad, new GamepadState {rightTrigger = 0.5f}, 2.0);
        InputSystem.Update();

        InputSystem.QueueStateEvent(gamepad, new GamepadState {rightTrigger = 0.75f}, 1.0);
        InputSystem.Update();

        Assert.That(gamepad.rightTrigger.ReadValue(), Is.EqualTo(0.5f).Within(0.000001));
    }

    private struct CustomNestedDeviceState : IInputStateTypeInfo
    {
        [InputControl(name = "button1", layout = "Button")]
        public int buttons;

        [InputControl(layout = "Axis")] public float axis2;

        public FourCC GetFormat()
        {
            return new FourCC('N', 'S', 'T', 'D');
        }
    }

    private struct CustomDeviceState : IInputStateTypeInfo
    {
        [InputControl(layout = "Axis")] public float axis;

        public CustomNestedDeviceState nested;

        public FourCC GetFormat()
        {
            return new FourCC('C', 'U', 'S', 'T');
        }
    }

    [InputControlLayout(stateType = typeof(CustomDeviceState))]
    private class CustomDevice : InputDevice
    {
        public AxisControl axis { get; private set; }

        protected override void FinishSetup(InputDeviceBuilder builder)
        {
            axis = builder.GetControl<AxisControl>(this, "axis");
            base.FinishSetup(builder);
        }
    }

    private class CustomDeviceWithUpdate : CustomDevice, IInputUpdateCallbackReceiver
    {
        public int onUpdateCallCount;
        public InputUpdateType onUpdateType;

        public void OnUpdate(InputUpdateType updateType)
        {
            ++onUpdateCallCount;
            onUpdateType = updateType;
            InputSystem.QueueStateEvent(this, new CustomDeviceState {axis = 0.234f});
        }
    }

    // We want devices to be able to "park" unused controls outside of the state
    // memory region that is being sent to the device in events.
    [Test]
    [Category("Events")]
    public void Events_CanSendSmallerStateToDeviceWithLargerState()
    {
        const string json = @"
            {
                ""name"" : ""TestLayout"",
                ""extend"" : ""CustomDevice"",
                ""controls"" : [
                    { ""name"" : ""extra"", ""layout"" : ""Button"" }
                ]
            }
        ";

        InputSystem.RegisterControlLayout<CustomDevice>();
        InputSystem.RegisterControlLayout(json);
        var device = (CustomDevice)InputSystem.AddDevice("TestLayout");

        InputSystem.QueueStateEvent(device, new CustomDeviceState {axis = 0.5f});
        InputSystem.Update();

        Assert.That(device.axis.ReadValue(), Is.EqualTo(0.5).Within(0.000001));
    }

    private struct ExtendedCustomDeviceState : IInputStateTypeInfo
    {
        public CustomDeviceState baseState;
        public int extra;

        public FourCC GetFormat()
        {
            return baseState.GetFormat();
        }
    }

    // HIDs rely on this behavior as we may only use a subset of a HID's set of
    // controls and thus get state events that are larger than the device state
    // that we store for the HID.
    [Test]
    [Category("Events")]
    public void Events_CandSendLargerStateToDeviceWithSmallerState()
    {
        InputSystem.RegisterControlLayout<CustomDevice>();
        var device = (CustomDevice)InputSystem.AddDevice("CustomDevice");

        var state = new ExtendedCustomDeviceState();
        state.baseState.axis = 0.5f;
        InputSystem.QueueStateEvent(device, state);
        InputSystem.Update();

        Assert.That(device.axis.ReadValue(), Is.EqualTo(0.5).Within(0.000001));
    }

    [Test]
    [Category("Events")]
    public void Events_CanUpdateDeviceWithEventsFromUpdateCallback()
    {
        InputSystem.RegisterControlLayout<CustomDeviceWithUpdate>();
        var device = (CustomDeviceWithUpdate)InputSystem.AddDevice("CustomDeviceWithUpdate");

        InputSystem.Update();

        Assert.That(device.onUpdateCallCount, Is.EqualTo(1));
        Assert.That(device.onUpdateType, Is.EqualTo(InputUpdateType.Dynamic));
        Assert.That(device.axis.ReadValue(), Is.EqualTo(0.234).Within(0.000001));
    }
}
