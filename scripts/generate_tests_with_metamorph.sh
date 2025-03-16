#!/bin/bash

# Check if input file is provided
if [ $# -lt 2 ]; then
  echo "Usage: $0 <input_dafny_file> <seq-length-limit>"
  exit 1
fi

INPUT_FILE="$1"
LENGTH_LIMIT="$2"
INPUT_BASE="${INPUT_FILE%.dfy}"
INPUT_DIR_NAME=$(dirname $INPUT_FILE)
TEST_FILE="${INPUT_BASE}Tests.dfy"
SYNTHESIZED_FILE="${INPUT_BASE}Synthesized.dfy"
RESULTS_FILE="${INPUT_BASE}Results.txt"
HEURISTIC_FILE="${INPUT_DIR_NAME}/Heuristic"
EXTERN_FILE="${INPUT_DIR_NAME}/Externs.cs"

# Step 1: Generate tests
echo "Generating tests..."
start_time=$(date +%s)
dotnet Metamorph/dafny/Binaries/Dafny.dll generate-tests Block "$INPUT_FILE" --verbose --verification-time-limit 10000 --length-limit "$LENGTH_LIMIT" > "$TEST_FILE"
end_time=$(date +%s)
echo "Test Generation took $((end_time - start_time)) seconds"

# Step 2: Pretrain the synthesis tool
echo "Pretraining Metamorph..."
start_time=$(date +%s)
dotnet Metamorph/Binaries/Metamorph.dll --pretrain "$HEURISTIC_FILE" --input "$TEST_FILE"
end_time=$(date +%s)
echo "Pretraining took $((end_time - start_time)) seconds"

# Step 3: Copy the original file to a new file for modifications
cp "$TEST_FILE" "$SYNTHESIZED_FILE"

# Step 4: Extract names of methods to synthesize
SYNTHESIZE_NAMES=$(sed -n 's/.*{:synthesize "\(.*\)"}.*/\1/p' "$TEST_FILE")

# Step 5: Synthesize methods and replace placeholders
for NAME in $SYNTHESIZE_NAMES; do
  echo "Synthesizing method $NAME..."
  start_time=$(date +%s)
  SYNTHESIZED_CODE=$(dotnet Metamorph/Binaries/Metamorph.dll --loadPretrained "$HEURISTIC_FILE" --input "$TEST_FILE" --goal "$NAME")

  python3 -c "
import sys
import re

temp_file = '$SYNTHESIZED_FILE'
name = '$NAME'
synthesized_code = '''$SYNTHESIZED_CODE'''

with open(temp_file, 'r') as f:
    lines = f.readlines()

pattern = re.compile(r'^static method ' + re.escape(name) + r'\\b.*$')
found = False

with open(temp_file, 'w') as f:
    for line in lines:
        if pattern.match(line) and not found:
            f.write(synthesized_code + '\\n')
            found = True
        else:
            f.write(line)
" || { echo "Error during code replacement"; exit 1; }
  end_time=$(date +%s)
  echo "Synthesis of method $NAME completed in $((end_time - start_time)) seconds"
done

# Step 6: Run Dafny test and save results
echo "Running Dafny test on synthesized file..."
start_time=$(date +%s)
dotnet Metamorph/dafny/Binaries/Dafny.dll test "$SYNTHESIZED_FILE" --input "$EXTERN_FILE" > "$RESULTS_FILE"
end_time=$(date +%s)
echo "Test complied and executed in $((end_time - start_time)) seconds"

echo "Done. Results saved to $RESULTS_FILE"