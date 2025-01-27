// NDI Godot using C#
// Xero Wolf 2025

using Godot;
using NewTek;
using NewTek.NDI;
using System;
using System.Runtime.InteropServices;

public partial class Main_Old : Control
{
	[Export] string sourceName = "Godot NDI";
	[Export] Label label; //fps label
	[Export] int frameRate = 30;
	//A percentage used to adjust to rate at with NDI frames are sent
	[Export(PropertyHint.Range, "0.1,1.0,0.05")] float sendRate = 0.9f; // default is 90% 
	[Export] Godot.Timer updateTimer;
	[Export] SubViewport subViewport;
	
	
	byte[] imageData; // This will be used to hold the viewport data
	IntPtr bufferPtr; // pointer to video buffer
	

	NDIlib.video_frame_v2_t videoFrame; //Used to construct the NDI video

	IntPtr sendInstancePtr; // NDI sender

	GodotObject _object = new GodotObject(); // dummy object

	
	bool _ndiReady = false;
	bool _send = false;
	int _frameNumber = 0; //counter for adusting the send rate
	int _connections = 0;



	public override void _Ready()
	{
		// Used to keep the renderer active with the program is minimized
		DisplayServer.RegisterAdditionalOutput(_object);
		
		updateTimer.Timeout += UpdateTimer;

		// .Net interop doesn't handle UTF-8 strings, so do it manually
		// These must be freed later
		IntPtr sourceNamePtr = UTF.StringToUtf8(sourceName);

		IntPtr groupsNamePtr = IntPtr.Zero;

		// Not required, but "correct". (see the SDK documentation)
		if (!NDIlib.initialize())
		{
			// Cannot run NDI. Most likely because the CPU is not sufficient (see SDK documentation).
			// you can check this directly with a call to NDIlib_is_supported_CPU()
			GD.Print("Cannot run NDI");
			return;
		}
		else 
		{
			GD.Print("NDI Ready");
		}

		// Create an NDI source description using sourceNamePtr and it's clocked to the video.
		NDIlib.send_create_t createDesc = new NDIlib.send_create_t()
		{
			p_ndi_name = sourceNamePtr,
			p_groups = groupsNamePtr,
			clock_video = true,
			clock_audio = false
		};

		// We create the NDI finder instance
		sendInstancePtr = NDIlib.send_create(ref createDesc);

		// free the strings we allocated
		Marshal.FreeHGlobal(sourceNamePtr);
		Marshal.FreeHGlobal(groupsNamePtr);

		// did it succeed?
		if (sendInstancePtr == IntPtr.Zero)
		{
			GD.Print("Failed to create send instance");
			return;
		}
		
		// define our frame properties
		int xres = subViewport.Size.X;
		int yres = subViewport.Size.Y;
		int stride = xres * 4; // this is used in the SDK examples if using(xres * 32/*BGRA bpp*/ + 7) / 8; 
		int bufferSize = yres * stride;
		
		// allocate some memory for a video buffer
		bufferPtr = Marshal.AllocHGlobal((int)bufferSize);
		

		// We are going to create a 1280x720 progressive frame at 29.97Hz.
		videoFrame = new NDIlib.video_frame_v2_t()
		{
			// Resolution
			xres = xres,
			yres = yres,
			// Use RGBA video instead of BGRA as used in the NDI Examples
			FourCC = NDIlib.FourCC_type_e.FourCC_type_RGBA, // Oringinal FourCC_type_BGRA
			// The frame-rate
			frame_rate_N = frameRate * 1000, // my way of setting the frame rate this will give you 59.
			frame_rate_D = 1001,
			// The aspect ratio (16:9)
			picture_aspect_ratio = (16.0f / 9.0f),
			// This is a progressive frame
			frame_format_type = NDIlib.frame_format_type_e.frame_format_type_progressive,
			// Timecode.
			timecode = NDIlib.send_timecode_synthesize,
			// The video memory used for this frame
			p_data = bufferPtr,
			// The line to line stride of this image
			line_stride_in_bytes = stride,
			// no metadata
			p_metadata = IntPtr.Zero,
			// only valid on received frames
			timestamp = 0
		};


		updateTimer.Start();
		_ndiReady = true;
	}



	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		if (_ndiReady && _send)
		{
			ProcessFrames();
			label.Text = $"FPS:{Engine.GetFramesPerSecond().ToString()} | Send Rate: {(int)frameRate * sendRate} | Res:{subViewport.Size.X}x{subViewport.Size.Y}";
		}
	}

	// checks for a connection before processing and sending frames
	public void UpdateTimer()
	{
				
		_connections = NDIlib.send_get_no_connections(sendInstancePtr, 0);
		if (_connections > 0)
		{
			if (_send != true) 
			{
				_send = true;
				GD.Print($"NDI Connections {_connections} ");
			}
			
		}
		else
		{
			if (_send != false) 
			{
				_send = false;
			}
			GD.Print($"No Connections {_connections} ");
		}
		
	}

	//Gives some control over how often frames are processed and sent
	//Based on your use case you may only need to send frames when a
	//change is made as NDI always holds the last frame in the buffer
	public void ProcessFrames()
	{	
		//conversion so that the higher the send rate the less frames are skipped
		float actualSendRate = frameRate - (frameRate * sendRate);

		_frameNumber++;
		if (_frameNumber >= actualSendRate)
		{
			_frameNumber = 0;
			//Get the raw viewport data in bytes
			// Note this is relativly slow for video and it's NOT recommended
			// to do this every frame for high resolutions or frame rates
			// as it was not meant to be used in real time.
			imageData = subViewport.GetTexture().GetImage().GetData();

			//copy the viewport data to the memory allocated for use by NDI
			Marshal.Copy(imageData, 0, bufferPtr, imageData.Length);

			SendFrames();
		}
		
	}

		public void SendFrames()
	{

		// Get the tally state of this source (we poll it),
		NDIlib.tally_t NDI_tally = new NDIlib.tally_t();
		NDIlib.send_get_tally(sendInstancePtr, ref NDI_tally, 0);


		// We now submit the frame. Note that this call will be clocked so that we end up submitting 
		// at exactly 29.97fps.
		NDIlib.send_send_video_v2(sendInstancePtr, ref videoFrame);

	}

    public override void _ExitTree()
    {
        base._ExitTree();
		Cleanup();
    }

	// Free up memory allocations
	public void Cleanup()
	{	
		// Free dummy object
		DisplayServer.UnregisterAdditionalOutput(_object);
		_object.Free();

		// free our buffers
		Marshal.FreeHGlobal(bufferPtr);

		//Audio not used
		//Marshal.FreeHGlobal(audioFrame.p_data);

		// Destroy the NDI sender
		NDIlib.send_destroy(sendInstancePtr);

		// Not required, but "correct". (see the SDK documentation)
		NDIlib.destroy();

	}
}
