package audio

import (
	"bytes"
	"context"
	"encoding/binary"
	"fmt"
	"math"
	"sync"
	"time"

	"github.com/gen2brain/malgo"
)

// AudioSegment represents a recorded audio segment
type AudioSegment struct {
	Data       []byte        // Raw PCM samples
	SampleRate uint32        // 16000 Hz
	Channels   uint32        // 1 (mono)
	Duration   time.Duration
}

// Recorder manages audio recording
type Recorder struct {
	malgoCtx   *malgo.AllocatedContext
	device     *malgo.Device
	deviceID   string
	sampleRate uint32
	channels   uint32
	maxSeconds int

	mu        sync.Mutex
	buf       *bytes.Buffer
	recording bool
	startTime time.Time
}

// NewRecorder creates a new audio recorder with pre-initialized device
func NewRecorder(deviceID string, maxSeconds int) (*Recorder, error) {
	ctx, err := malgo.InitContext(nil, malgo.ContextConfig{}, nil)
	if err != nil {
		return nil, fmt.Errorf("failed to initialize malgo context: %w", err)
	}

	r := &Recorder{
		malgoCtx:   ctx,
		deviceID:   deviceID,
		sampleRate: 16000,
		channels:   1,
		maxSeconds: maxSeconds,
		buf:        new(bytes.Buffer),
	}

	// Pre-initialize the audio device for instant recording start
	if err := r.initDevice(); err != nil {
		ctx.Uninit()
		ctx.Free()
		return nil, fmt.Errorf("failed to initialize audio device: %w", err)
	}

	return r, nil
}

// initDevice initializes and starts the audio device (called once at startup)
func (r *Recorder) initDevice() error {
	deviceConfig := malgo.DefaultDeviceConfig(malgo.Capture)
	deviceConfig.Capture.Format = malgo.FormatS16
	deviceConfig.Capture.Channels = r.channels
	deviceConfig.SampleRate = r.sampleRate
	deviceConfig.Alsa.NoMMap = 1

	// Data callback - always running, but only buffers when recording flag is true
	onData := func(pOutputSample, pInputSamples []byte, framecount uint32) {
		r.mu.Lock()
		defer r.mu.Unlock()

		// Only buffer audio data when actively recording
		if !r.recording {
			return
		}

		// Check if we've exceeded max duration
		if time.Since(r.startTime) > time.Duration(r.maxSeconds)*time.Second {
			r.recording = false
			return
		}

		// Write audio data to buffer
		r.buf.Write(pInputSamples)
	}

	var err error
	r.device, err = malgo.InitDevice(r.malgoCtx.Context, deviceConfig, malgo.DeviceCallbacks{
		Data: onData,
	})
	if err != nil {
		return fmt.Errorf("failed to initialize device: %w", err)
	}

	if err := r.device.Start(); err != nil {
		r.device.Uninit()
		r.device = nil
		return fmt.Errorf("failed to start device: %w", err)
	}

	return nil
}

// Start begins buffering audio (device is already running)
func (r *Recorder) Start(ctx context.Context) error {
	r.mu.Lock()
	defer r.mu.Unlock()

	if r.recording {
		return fmt.Errorf("already recording")
	}

	r.buf.Reset()
	r.recording = true
	r.startTime = time.Now()

	return nil
}

// Stop stops buffering and returns the audio segment (device stays running)
func (r *Recorder) Stop() (AudioSegment, error) {
	r.mu.Lock()
	defer r.mu.Unlock()

	if !r.recording {
		return AudioSegment{}, fmt.Errorf("not recording")
	}

	r.recording = false

	duration := time.Since(r.startTime)
	// Make a copy of the buffer data
	data := make([]byte, r.buf.Len())
	copy(data, r.buf.Bytes())

	return AudioSegment{
		Data:       data,
		SampleRate: r.sampleRate,
		Channels:   r.channels,
		Duration:   duration,
	}, nil
}

// Close releases resources
func (r *Recorder) Close() error {
	r.mu.Lock()
	defer r.mu.Unlock()

	if r.device != nil {
		r.device.Stop()
		r.device.Uninit()
		r.device = nil
	}

	if r.malgoCtx != nil {
		_ = r.malgoCtx.Uninit()
		r.malgoCtx.Free()
		r.malgoCtx = nil
	}

	return nil
}

// CalculateRMS calculates the Root Mean Square audio level
// Returns a value representing the average amplitude of the audio
// Typical values: silence < 500, quiet speech ~ 1000-2000, normal speech ~ 2000-5000
func (seg *AudioSegment) CalculateRMS() float64 {
	if len(seg.Data) == 0 {
		return 0
	}

	// Audio is 16-bit PCM, so we need to read 2 bytes per sample
	numSamples := len(seg.Data) / 2
	if numSamples == 0 {
		return 0
	}

	var sumSquares float64
	for i := 0; i < numSamples; i++ {
		// Read 16-bit little-endian sample
		sampleBytes := seg.Data[i*2 : i*2+2]
		sample := int16(binary.LittleEndian.Uint16(sampleBytes))
		sumSquares += float64(sample) * float64(sample)
	}

	return math.Sqrt(sumSquares / float64(numSamples))
}

// ToWAV converts the audio segment to WAV format
func (seg *AudioSegment) ToWAV() ([]byte, error) {
	buf := new(bytes.Buffer)

	// WAV header
	dataSize := uint32(len(seg.Data))
	bitsPerSample := uint16(16)
	blockAlign := uint16(seg.Channels * uint32(bitsPerSample) / 8)
	byteRate := seg.SampleRate * uint32(blockAlign)

	// RIFF header
	buf.WriteString("RIFF")
	binary.Write(buf, binary.LittleEndian, uint32(36+dataSize))
	buf.WriteString("WAVE")

	// fmt chunk
	buf.WriteString("fmt ")
	binary.Write(buf, binary.LittleEndian, uint32(16)) // Chunk size
	binary.Write(buf, binary.LittleEndian, uint16(1))  // Audio format (PCM)
	binary.Write(buf, binary.LittleEndian, uint16(seg.Channels))
	binary.Write(buf, binary.LittleEndian, seg.SampleRate)
	binary.Write(buf, binary.LittleEndian, byteRate)
	binary.Write(buf, binary.LittleEndian, blockAlign)
	binary.Write(buf, binary.LittleEndian, bitsPerSample)

	// data chunk
	buf.WriteString("data")
	binary.Write(buf, binary.LittleEndian, dataSize)
	buf.Write(seg.Data)

	return buf.Bytes(), nil
}
