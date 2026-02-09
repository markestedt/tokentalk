package audio

import (
	"bytes"
	"context"
	"encoding/binary"
	"fmt"
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

// NewRecorder creates a new audio recorder
func NewRecorder(deviceID string, maxSeconds int) (*Recorder, error) {
	ctx, err := malgo.InitContext(nil, malgo.ContextConfig{}, nil)
	if err != nil {
		return nil, fmt.Errorf("failed to initialize malgo context: %w", err)
	}

	return &Recorder{
		malgoCtx:   ctx,
		deviceID:   deviceID,
		sampleRate: 16000,
		channels:   1,
		maxSeconds: maxSeconds,
		buf:        new(bytes.Buffer),
	}, nil
}

// Start begins recording audio
func (r *Recorder) Start(ctx context.Context) error {
	r.mu.Lock()
	defer r.mu.Unlock()

	if r.recording {
		return fmt.Errorf("already recording")
	}

	r.buf.Reset()
	r.recording = true
	r.startTime = time.Now()

	deviceConfig := malgo.DefaultDeviceConfig(malgo.Capture)
	deviceConfig.Capture.Format = malgo.FormatS16
	deviceConfig.Capture.Channels = r.channels
	deviceConfig.SampleRate = r.sampleRate
	deviceConfig.Alsa.NoMMap = 1

	// Data callback
	onData := func(pOutputSample, pInputSamples []byte, framecount uint32) {
		r.mu.Lock()
		defer r.mu.Unlock()

		if !r.recording {
			return
		}

		// Check if we've exceeded max duration
		if time.Since(r.startTime) > time.Duration(r.maxSeconds)*time.Second {
			r.recording = false
			if r.device != nil {
				r.device.Stop()
			}
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
		r.recording = false
		return fmt.Errorf("failed to initialize device: %w", err)
	}

	if err := r.device.Start(); err != nil {
		r.recording = false
		return fmt.Errorf("failed to start device: %w", err)
	}

	return nil
}

// Stop stops recording and returns the audio segment
func (r *Recorder) Stop() (AudioSegment, error) {
	r.mu.Lock()
	defer r.mu.Unlock()

	if !r.recording {
		return AudioSegment{}, fmt.Errorf("not recording")
	}

	r.recording = false

	if r.device != nil {
		r.device.Uninit()
		r.device = nil
	}

	duration := time.Since(r.startTime)
	data := r.buf.Bytes()

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
