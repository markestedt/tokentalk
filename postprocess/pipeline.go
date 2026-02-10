package postprocess

import (
	"context"
	"log/slog"
)

// Processor is a function that transforms text
type Processor func(ctx context.Context, text string) (string, error)

// Pipeline runs a series of processors in sequence
type Pipeline struct {
	processors []Processor
}

// NewPipeline creates a new processing pipeline
func NewPipeline(processors ...Processor) *Pipeline {
	return &Pipeline{
		processors: processors,
	}
}

// Process runs all processors in sequence
func (p *Pipeline) Process(ctx context.Context, text string) (string, error) {
	result := text
	var err error

	for i, proc := range p.processors {
		result, err = proc(ctx, result)
		if err != nil {
			slog.Error("Processor failed", "index", i, "error", err)
			return result, err
		}
	}

	return result, nil
}

// AddProcessor adds a processor to the pipeline
func (p *Pipeline) AddProcessor(proc Processor) {
	p.processors = append(p.processors, proc)
}
