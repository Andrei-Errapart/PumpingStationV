/*
 * To change this template, choose Tools | Templates
 * and open the template in the editor.
 */
package plcmaster;

import java.io.*;

/** File utilities.
 *
 * @author Andrei
 */
public abstract class FileUtils {
    /** Read contents of the file using UTF-8 encoding.
     * 
     * @param Filename File to be read.
     * @return Contents as a string.
     * @throws Exception IO or conversion errors.
     */
    public static String ReadFileAsString(String Filename) throws Exception
    {
        File file = new File(Filename);
        int file_length = (int)file.length();
        byte[] b = new byte[file_length];
        InputStream in = new FileInputStream(Filename);
        try
        {
            int so_far = 0;
            while (so_far < file_length)
            {
                int this_round = in.read(b, so_far, file_length - so_far);
                if (this_round < 0)
                {
                    // FIXME: what do do?
                    break;
                }
                so_far += this_round;
            }
        }
        finally 
        {
            in.close();
        }
        return new String(b, "UTF-8");
    }
    
    public static byte[] ReadFileAsBytes(String Filename) throws Exception
    {
        RandomAccessFile fin = new RandomAccessFile(Filename, "r");
        try
        {
            int fin_length = (int)fin.length();
            byte[] fin_buffer = new byte[fin_length];
            int this_round = 0;
            for (int so_far=0; so_far<fin_length; so_far += this_round)
            {
                this_round = fin.read(fin_buffer, so_far, fin_length - so_far);
                if (this_round<0)
                {
                    // oops.
                    throw new ApplicationException("Unexpected error when reading file'" + Filename + "'.");
                }
            }
            return fin_buffer;
        }
        finally
        {
            fin.close();
        }
    }
}
