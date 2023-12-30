# EEG

![console](https://raw.githubusercontent.com/georgemihaila/eeg/main/.img/Screenshot_20231230_230848.png?token=GHSAT0AAAAAACKY7KDBQAY7LJIXGYKB7BVOZMQSV3Q)

A simple EEG acquisition server based on [Olimex](https://www.olimex.com/Products/EEG/OpenEEG/EEG-SMT/open-source-hardware) hardware that logs packets to an InfluxDB instance.

Data can be displayed in an Influx notebook

![](https://raw.githubusercontent.com/georgemihaila/eeg/main/.img/Screenshot_20231230_230552.png?token=GHSAT0AAAAAACKY7KDAJAJOZT4GHWUJNELYZMQSVRA)

or it can be analyzed further using [scipy](https://github.com/georgemihaila/eeg/blob/main/notebook/Data%20analysis.ipynb); the following is an FFT analysis of around 2h of sleep data 

![](https://github.com/georgemihaila/eeg/blob/main/.img/IMG-20231118-WA0000.jpg)
