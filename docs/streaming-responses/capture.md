# Capture: agy stream-json output

Two paid runs (about 17,000 input tokens each). Run in a new empty directory, not in this repo. Nothing here is a secret; the files hold only the model's answer to these prompts.

```
mkdir -p ~/agy-capture && cd ~/agy-capture
agy -p "Reply with the single word: ok" --output-format stream-json --model gemini-3.8-flash-low > one-step.ndjson 2> one-step.err
agy -p "Create a file notes.txt containing the word hello, then tell me in one sentence what you did." --output-format stream-json --model gemini-3.8-flash-low > with-tool.ndjson 2> with-tool.err
```

Then put the four files, or just the `.ndjson` pair, under `docs/streaming-responses/capture/` and tell me. I need: the `result` event fields, whether `text_delta` is incremental or cumulative, the `step_type` values, and whether the second run gives more than one answer step.
