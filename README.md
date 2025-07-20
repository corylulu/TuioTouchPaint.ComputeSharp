
# TUIO Touch Paint Application

## Project Description

A touch paint application capable of rendering touch data from TUIO and Mouse/Touch sources utilizing custom brushes with multiple frames making up a single brush akin to Photoshop brushes.

## Requirements:

- Multi-frame brush support (several brush textures that change continously while drawing)
- Configurability for brush size and color on a per touch input basis
- The TUIO touches can be from multiple TUIO ports which represent different color brushes on each TUIO port received
- Touches can be configured to auto-fade after a certain duration, so the beginning part of their brush strokes will start to fade away after X seconds, even as the touch is still drawing
- New brush strokes will render on top of ones drawn previously.
- The whole canvas must have a fullscreen mode
- Should be able to render at high framerates at large resolutions
- The ability to clear regions of paint manually

## Phase two additional features:

- The ability to remap a drawing space of the TUIO inputs... For example: the normalized TUIO signal might be 32:9 and I would like to be able to configure the left half of the TUIO range (X1:0.0, Y1:0.0, X2:0.5, Y2:1.0) to the canvas as (X1:0.0, Y1:0.0, X2:1.0, Y2:0.5) and the right half of the TUIO range (X1:0.5, Y1:0.0, X2:1.0, Y2:1.0) to the canvas as (X1:0.0, Y1:0.5, X2:1.0, Y2:1.0). This would need to be done in a way that doesn't impact brush strokes that cross over those points.
