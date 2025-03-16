import glob
import os
import re
import numpy as np
import matplotlib.pyplot as plt
import matplotlib
from collections import defaultdict
import sys
import argparse

# Use Type-1 fonts instead of Type-3
matplotlib.rcParams['pdf.fonttype'] = 42  # Ensures TrueType fonts are used in PDFs
matplotlib.rcParams['ps.fonttype'] = 42   # Ensures TrueType fonts are used in PS

# Set a font that works well
matplotlib.rcParams['font.family'] = 'serif'  # Or 'Times New Roman' for IEEE submissions
matplotlib.rcParams['text.usetex'] = False    # Disable LaTeX rendering to avoid unexpected Type-3 fonts


def parse_time_string(time_str):
    """Convert a time string formatted as 'hh:mm:ss.microseconds' to total seconds."""
    h, m, s = time_str.split(':')
    total_seconds = int(h) * 3600 + int(m) * 60 + float(s)
    return total_seconds

def extract_benchmark_name(filename):
    """Extract the benchmark name from the filename."""
    basename = os.path.basename(filename)
    # Remove date and time
    parts = basename.split('_', 2)
    if len(parts) >= 3:
        # The third part is the path with '$' as separator
        path_with_dollar = parts[2]
        # Remove the file extension
        path_with_dollar = path_with_dollar.replace('.dfy.log', '')
        # Split the path into parts
        path_parts = path_with_dollar.split('$')
        try:
            bench_index = path_parts.index('Benchmarks') + 1
            benchmark_name = path_parts[bench_index]
            return benchmark_name
        except ValueError:
            pass
    return None

def extract_times_from_log(file_path):
    """Extract time data from a log file and compute percentages."""
    with open(file_path, 'r') as f:
        lines = f.readlines()
        # Get the last 10 lines to find the time breakdown
        last_lines = lines[-10:]
        
        total_time = -1
        regular_time = -1
        simplify_time = -1
        heuristic_time = -1
        
        for line in last_lines:
            line = line.strip()
            if 'Total time spend on synthesis:' in line:
                match = re.search(r'Total time spend on synthesis: (.+)', line)
                if match:
                    time_str = match.group(1)
                    total_time = parse_time_string(time_str)
            elif 'Total number of Regular queries to Dafny:' in line:
                match = re.search(r'Total number of Regular queries to Dafny: \d+ \((.+)\)', line)
                if match:
                    time_str = match.group(1)
                    regular_time = parse_time_string(time_str)
            elif 'Total number of Simplify queries to Dafny:' in line:
                match = re.search(r'Total number of Simplify queries to Dafny: \d+ \((.+)\)', line)
                if match:
                    time_str = match.group(1)
                    simplify_time = parse_time_string(time_str)
            elif 'Total number of Heuristic queries to Dafny:' in line:
                match = re.search(r'Total number of Heuristic queries to Dafny: \d+ \((.+)\)', line)
                if match:
                    time_str = match.group(1)
                    heuristic_time = parse_time_string(time_str)
                    
        if total_time != -1 and regular_time != -1 and simplify_time != -1 and heuristic_time != -1:
            other_time = total_time - (regular_time + simplify_time + heuristic_time)
            # Convert to percentages
            regular_pct = (regular_time / total_time) * 100
            simplify_pct = (simplify_time / total_time) * 100
            heuristic_pct = (heuristic_time / total_time) * 100
            other_pct = (other_time / total_time) * 100
            return regular_pct, simplify_pct, heuristic_pct, other_pct
        else:
            # Missing data; ignore this log file
            return None

def main(log_dir):
    # Step 1: Get all log filenames while excluding certain files
    log_files = glob.glob(f'{log_dir}/**/*.log', recursive=True)
    log_files = [f for f in log_files if len([prohibit for prohibit in 
        ["Definitions", "StartUp", # These are not log files with regular synthesis problems
         # we don't want the first two (one? three?) problems, since they are simple and disproportionally affect everything]
         "Problem00", "Problem01"]
        if prohibit in f]) == 0]
    
    # Step 2: Group filenames by benchmark
    benchmark_files = defaultdict(list)
    for f in log_files:
        benchmark_name = extract_benchmark_name(f)
        if benchmark_name:
            benchmark_files[benchmark_name].append(f)
    
    # Step 3: Process each benchmark's log files
    benchmark_data = {}
    for benchmark, files in benchmark_files.items():
        regular_pcts = []
        simplify_pcts = []
        heuristic_pcts = []
        other_pcts = []
        
        for f in sorted(files):
            times = extract_times_from_log(f)
            if times:
                regular_pct, simplify_pct, heuristic_pct, other_pct = times
                regular_pcts.append(regular_pct)
                simplify_pcts.append(simplify_pct)
                heuristic_pcts.append(heuristic_pct)
                other_pcts.append(other_pct)
        
        if regular_pcts:
            # Compute mean and standard deviation
            benchmark_data[benchmark] = {
                'regular_mean': np.mean(regular_pcts),
                'regular_std': np.std(regular_pcts),
                'simplify_mean': np.mean(simplify_pcts),
                'simplify_std': np.std(simplify_pcts),
                'heuristic_mean': np.mean(heuristic_pcts),
                'heuristic_std': np.std(heuristic_pcts),
                'other_mean': np.mean(other_pcts),
                'other_std': np.std(other_pcts)
            }

    # Hardcoded order of benchmarks
    desired_order = [
        "BinaryTree", "Firewall", "SocialNetwork", "EVM",
        "FreezableArray", "Queue", "DoublyLinkedList"
    ]

    # Generate the sorted list
    benchmarks = list(sorted(
        benchmark_data.keys(),
        key=lambda x: (desired_order.index(x) if x in desired_order else len(desired_order) + 1, x)
    ))

    num_benchmarks = len(benchmarks)
    
    regular_means = [benchmark_data[b]['regular_mean'] for b in benchmarks]
    simplify_means = [benchmark_data[b]['simplify_mean'] for b in benchmarks]
    heuristic_means = [benchmark_data[b]['heuristic_mean'] for b in benchmarks]
    other_means = [benchmark_data[b]['other_mean'] for b in benchmarks]
    
    regular_stds = [benchmark_data[b]['regular_std'] for b in benchmarks]
    simplify_stds = [benchmark_data[b]['simplify_std'] for b in benchmarks]
    heuristic_stds = [benchmark_data[b]['heuristic_std'] for b in benchmarks]
    other_stds = [benchmark_data[b]['other_std'] for b in benchmarks]
    
    ind = np.arange(num_benchmarks)  # x locations for the groups
    width = 0.5  # width of the bars
    
    # Cumulative sums for stacking
    regular_cum = regular_means
    simplify_cum = np.add(regular_means, simplify_means)
    heuristic_cum = np.add(simplify_cum, heuristic_means)
    total_cum = np.add(heuristic_cum, other_means)
    
    plt.figure(figsize=(28, 16))
    
    # Plot stacked bars
    plt.bar(ind, regular_means, width, label='Calls to PreconditionFor', color="#264653")
    plt.bar(ind, simplify_means, width, bottom=regular_means, label='Calls to WeakenPrecondition', color="#2A9D8F")
    plt.bar(ind, heuristic_means, width, bottom=simplify_cum, label='Initial State Constraint Analysis', color="#d9d562")
    plt.bar(ind, other_means, width, bottom=heuristic_cum, label='Other Tasks', color="#e86146")
    
    # Now, plot error bars at the transition points between segments
    plt.errorbar(ind + 0.2 * width, regular_cum, yerr=regular_stds, fmt='none', ecolor='black', capsize=3, linewidth=3)
    plt.errorbar(ind + 0.1 * width, simplify_cum, yerr=simplify_stds, fmt='none', ecolor='black', capsize=3, linewidth=3)
    plt.errorbar(ind - 0.1 * width, heuristic_cum, yerr=heuristic_stds, fmt='none', ecolor='black', capsize=3, linewidth=3)
    plt.errorbar(ind - 0.2 * width, total_cum, yerr=other_stds, fmt='none', ecolor='black', capsize=3, linewidth=3)
    
    plt.ylabel('Percentage of Total Time', fontsize=50)
    plt.xticks(ind, benchmarks, rotation=10,  fontsize=40)
    plt.yticks(np.arange(0, 101, 10),  fontsize=40)
    # Retrieve the legend handles and labels, reverse them, and then pass to plt.legend
    handles, labels = plt.gca().get_legend_handles_labels()
    plt.legend(handles[::-1], labels[::-1], loc="lower left", fontsize=40)
    plt.tight_layout()
    plt.savefig(f'Figure9.pdf')


if __name__ == "__main__":
    p = argparse.ArgumentParser(description="Generate running time breakdown figure from the logs Metamorph produces.")
    p.add_argument('-logs',
                   help=f"The directory with logs.",
                   required=True)
    args = p.parse_args(sys.argv[1:])
    main(args.logs)