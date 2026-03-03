FILE_OPERATIONS_SKILL_V1

Determine the file operation type and extract parameters.
Return ONLY valid JSON in this format: {"type":"read"|"write","path":"filename.ext","content":"content if write, null if read"}
- type: 'read' for reading/showing/outputting files, 'write' for creating/writing files
- path: the exact filename mentioned
- content: file content if writing, null if reading
