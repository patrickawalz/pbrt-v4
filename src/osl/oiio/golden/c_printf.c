#include <stdio.h>
int main(){
 printf("%g\n",100000.0f); printf("%g\n",1000000.0f); printf("%g\n",0.0001f); printf("%g\n",0.00001f);
 printf("%e\n",12345.678f); printf("%5.2f|\n",3.14159f); printf("%-8s|\n","ab"); printf("%+d\n",5);
 printf("%08.3f\n",-3.14159f); printf("%%\n"); printf("%x\n",255); printf("%#X\n",255); printf("%o\n",8);
 printf("%5d|%-5d|\n",42,42); printf("%.3s\n","abcdef"); printf("%10.4e\n",0.000123456f);
}
